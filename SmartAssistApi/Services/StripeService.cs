using Stripe;
using Stripe.Checkout;

namespace SmartAssistApi.Services;

public class StripeService
{
    private readonly IConfiguration _config;
    private readonly UsageService _usageService;
    private readonly IStripeApiClient _stripeApiClient;
    private readonly ILogger<StripeService> _logger;

    public StripeService(
        IConfiguration config,
        UsageService usageService,
        IStripeApiClient stripeApiClient,
        ILogger<StripeService> logger)
    {
        _config = config;
        _usageService = usageService;
        _stripeApiClient = stripeApiClient;
        _logger = logger;

        StripeConfiguration.ApiKey = config["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey missing");
    }

    public async Task<string> CreateCheckoutSessionAsync(
        string userId,
        string userEmail,
        string plan,
        string? correlationId = null)
    {
        var priceId = plan == "premium"
            ? _config["Stripe:PremiumPriceId"]
            : _config["Stripe:ProPriceId"];

        if (string.IsNullOrWhiteSpace(priceId))
            throw new InvalidOperationException($"Price ID for {plan} not configured");

        if (!priceId.StartsWith("price_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Invalid Stripe price ID for {plan}. Expected a value starting with price_.");

        var successUrl = ResolveSuccessUrl();
        var cancelUrl = ResolveCancelUrl();

        var metadata = new Dictionary<string, string>
        {
            ["userId"] = userId,
            ["plan"] = plan,
        };

        if (!string.IsNullOrWhiteSpace(userEmail))
            metadata["email"] = userEmail;

        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = ["card"],
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ],
            Metadata = metadata,
            CustomerEmail = userEmail,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
        };

        var session = await _stripeApiClient.CreateCheckoutSessionAsync(options);
        if (string.IsNullOrWhiteSpace(session.Url))
            throw new InvalidOperationException("Stripe checkout session was created without a URL.");

        _logger.LogInformation(
            "Stripe checkout session created. CorrelationId {CorrelationId} UserId {UserId} Plan {Plan} SessionId {SessionId}",
            correlationId,
            userId,
            plan,
            session.Id);

        return session.Url;
    }

    public async Task<string> CreatePortalSessionAsync(string customerId)
    {
        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = ResolvePortalReturnUrl(),
        };

        var session = await _stripeApiClient.CreateBillingPortalSessionAsync(options);
        if (string.IsNullOrWhiteSpace(session.Url))
            throw new InvalidOperationException("Stripe billing portal session was created without a URL.");

        return session.Url;
    }

    public async Task HandleWebhookAsync(string payload, string stripeSignature)
    {
        var webhookSecret = _config["Stripe:WebhookSecret"]
            ?? throw new InvalidOperationException("Stripe:WebhookSecret missing");

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, stripeSignature, webhookSecret);
        }
        catch (StripeException ex)
        {
            throw new StripeWebhookSignatureException(
                $"Webhook signature verification failed: {ex.Message}", ex);
        }

        await HandleStripeEventAsync(stripeEvent);
    }

    public async Task HandleStripeEventAsync(Event stripeEvent)
    {
        if (string.IsNullOrWhiteSpace(stripeEvent.Id))
            throw new InvalidOperationException("Stripe event id is missing.");

        var isFirstDelivery = await _usageService.TryAcquireStripeEventAsync(stripeEvent.Id);
        if (!isFirstDelivery)
        {
            _logger.LogInformation(
                "Duplicate Stripe webhook event ignored. EventId {StripeEventId} Type {StripeEventType}",
                stripeEvent.Id,
                stripeEvent.Type);
            return;
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                if (stripeEvent.Data.Object is Stripe.Checkout.Session checkoutSession)
                {
                    await HandleCheckoutCompletedAsync(stripeEvent, checkoutSession);
                    return;
                }
                break;

            case EventTypes.CustomerSubscriptionDeleted:
            case EventTypes.CustomerSubscriptionUpdated:
                if (stripeEvent.Data.Object is Subscription subscription)
                {
                    await HandleSubscriptionChangedAsync(stripeEvent, subscription);
                    return;
                }
                break;

            case EventTypes.InvoicePaid:
                if (stripeEvent.Data.Object is Invoice invoice)
                {
                    await HandleInvoicePaidAsync(stripeEvent, invoice);
                    return;
                }
                break;
        }

        _logger.LogWarning(
            "Stripe webhook event received with unsupported payload. EventId {StripeEventId} Type {StripeEventType}",
            stripeEvent.Id,
            stripeEvent.Type);
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent, Stripe.Checkout.Session session)
    {
        var sessionId = session.Id ?? string.Empty;
        var userId = session.Metadata?.GetValueOrDefault("userId");
        var plan = session.Metadata?.GetValueOrDefault("plan");

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(plan))
        {
            _logger.LogError(
                "Stripe checkout metadata missing. EventId {StripeEventId} SessionId {StripeSessionId} UserIdMeta {UserIdMeta} PlanMeta {PlanMeta}",
                stripeEvent.Id,
                sessionId,
                userId,
                plan);

            throw new InvalidOperationException(
                $"Stripe checkout metadata missing for event {stripeEvent.Id} and session {sessionId}.");
        }

        if (plan is not ("premium" or "pro"))
        {
            _logger.LogError(
                "Stripe checkout metadata contains unsupported plan. EventId {StripeEventId} SessionId {StripeSessionId} Plan {Plan}",
                stripeEvent.Id,
                sessionId,
                plan);

            throw new InvalidOperationException(
                $"Unsupported Stripe plan metadata '{plan}' for event {stripeEvent.Id}.");
        }

        await _usageService.SetPlanAsync(userId, plan);

        if (!string.IsNullOrWhiteSpace(session.CustomerId))
            await _usageService.SetStripeCustomerIdAsync(userId, session.CustomerId);

        var audit = new StripeWebhookAuditRecord(
            stripeEvent.Id,
            stripeEvent.Type,
            sessionId,
            userId,
            plan,
            DateTimeOffset.UtcNow.ToString("O"),
            session.CustomerId,
            session.SubscriptionId,
            "upgraded");

        await _usageService.RecordStripeWebhookAuditAsync(audit);

        _logger.LogInformation(
            "Stripe checkout completed and plan updated. EventId {StripeEventId} SessionId {StripeSessionId} UserId {UserId} Plan {Plan} CustomerId {CustomerId}",
            stripeEvent.Id,
            sessionId,
            userId,
            plan,
            session.CustomerId);
    }

    private async Task HandleSubscriptionChangedAsync(Event stripeEvent, Subscription subscription)
    {
        if (!string.Equals(subscription.Status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug(
                "Stripe subscription update ignored because status is not canceled. EventId {StripeEventId} SubscriptionId {SubscriptionId} Status {Status}",
                stripeEvent.Id,
                subscription.Id,
                subscription.Status);
            return;
        }

        if (string.IsNullOrWhiteSpace(subscription.CustomerId))
        {
            _logger.LogError(
                "Stripe subscription cancellation missing customer id. EventId {StripeEventId} SubscriptionId {SubscriptionId}",
                stripeEvent.Id,
                subscription.Id);
            throw new InvalidOperationException($"Stripe subscription event {stripeEvent.Id} has no customer id.");
        }

        var userId = await _usageService.GetUserIdByStripeCustomerIdAsync(subscription.CustomerId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogError(
                "No user mapping found for Stripe customer during cancellation. EventId {StripeEventId} CustomerId {CustomerId}",
                stripeEvent.Id,
                subscription.CustomerId);
            throw new InvalidOperationException(
                $"No user mapping found for Stripe customer {subscription.CustomerId} (event {stripeEvent.Id}).");
        }

        await _usageService.SetPlanAsync(userId, "free");

        var audit = new StripeWebhookAuditRecord(
            stripeEvent.Id,
            stripeEvent.Type,
            null,
            userId,
            "free",
            DateTimeOffset.UtcNow.ToString("O"),
            subscription.CustomerId,
            subscription.Id,
            "downgraded");

        await _usageService.RecordStripeWebhookAuditAsync(audit);

        _logger.LogInformation(
            "Stripe subscription canceled and user downgraded. EventId {StripeEventId} UserId {UserId} CustomerId {CustomerId}",
            stripeEvent.Id,
            userId,
            subscription.CustomerId);
    }

    private async Task HandleInvoicePaidAsync(Event stripeEvent, Invoice invoice)
    {
        string? userId = null;

        if (!string.IsNullOrWhiteSpace(invoice.CustomerId))
            userId = await _usageService.GetUserIdByStripeCustomerIdAsync(invoice.CustomerId);

        var audit = new StripeWebhookAuditRecord(
            stripeEvent.Id,
            stripeEvent.Type,
            null,
            userId,
            null,
            DateTimeOffset.UtcNow.ToString("O"),
            invoice.CustomerId,
            null,
            "invoice_paid");

        await _usageService.RecordStripeWebhookAuditAsync(audit);

        _logger.LogInformation(
            "Stripe invoice paid event recorded. EventId {StripeEventId} CustomerId {CustomerId} UserId {UserId}",
            stripeEvent.Id,
            invoice.CustomerId,
            userId);
    }

    /// <summary>
    /// Confirms a premium plan by reading the completed checkout session directly from Stripe.
    /// This is used as a self-service fallback when the webhook hasn't fired yet.
    /// </summary>
    public async Task<string> ConfirmPlanFromSessionAsync(string authedUserId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID must not be empty.", nameof(sessionId));

        var session = await _stripeApiClient.GetCheckoutSessionAsync(sessionId);

        // Verify the session belongs to the authenticated user
        var sessionUserId = session.Metadata?.GetValueOrDefault("userId");
        if (!string.Equals(sessionUserId, authedUserId, StringComparison.Ordinal))
            throw new InvalidOperationException("Session does not belong to the authenticated user.");

        if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return "free"; // Payment not completed

        var plan = session.Metadata?.GetValueOrDefault("plan");
        if (plan is not ("premium" or "pro"))
            throw new InvalidOperationException($"Unsupported plan in session metadata: '{plan}'.");

        await _usageService.SetPlanAsync(authedUserId, plan);

        if (!string.IsNullOrWhiteSpace(session.CustomerId))
            await _usageService.SetStripeCustomerIdAsync(authedUserId, session.CustomerId);

        _logger.LogInformation(
            "Plan confirmed via checkout session. UserId {UserId} Plan {Plan} SessionId {SessionId}",
            authedUserId, plan, sessionId);

        return plan;
    }

    private string ResolveSuccessUrl()
    {
        var configured = _config["Frontend:SuccessUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var baseUrl = _config["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl missing");
        // {CHECKOUT_SESSION_ID} is replaced by Stripe with the actual session ID
        return $"{baseUrl}/profile?upgraded=true&session_id={{CHECKOUT_SESSION_ID}}";
    }

    private string ResolveCancelUrl()
    {
        var configured = _config["Frontend:CancelUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var baseUrl = _config["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl missing");
        return $"{baseUrl}/pricing?cancelled=true";
    }

    private string ResolvePortalReturnUrl()
    {
        var configured = _config["Frontend:PortalReturnUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var baseUrl = _config["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl missing");
        return $"{baseUrl}/profile";
    }
}

public sealed class StripeWebhookSignatureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
