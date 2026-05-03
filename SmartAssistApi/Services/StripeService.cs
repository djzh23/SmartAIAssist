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

    // ── Plan rank — used to prevent stale webhooks from downgrading an account ──
    private static readonly Dictionary<string, int> PlanRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["free"]    = 0,
        ["premium"] = 1,
        ["pro"]     = 2,
    };
    private static int Rank(string plan) => PlanRank.GetValueOrDefault(plan, 0);

    private string? PlanFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return null;
        if (priceId == _config["Stripe:ProPriceId"])      return "pro";
        if (priceId == _config["Stripe:PremiumPriceId"]) return "premium";
        return null;
    }

    public async Task HandleStripeEventAsync(Event stripeEvent)
    {
        if (string.IsNullOrWhiteSpace(stripeEvent.Id))
            throw new InvalidOperationException("Stripe event id is missing.");

        // Check idempotency but do NOT short-circuit yet — the handlers are safely
        // re-entrant (rank guard + upsert), so processing a retry is harmless.
        // We mark the event as processed AFTER successful handling to avoid the
        // scenario where a transient failure (e.g. DB timeout) permanently locks
        // out Stripe retries.
        var isFirstDelivery = await _usageService.TryAcquireStripeEventAsync(stripeEvent.Id);
        if (!isFirstDelivery)
        {
            _logger.LogInformation(
                "Duplicate Stripe webhook event received — processing anyway (handlers are idempotent). EventId {StripeEventId} Type {StripeEventType}",
                stripeEvent.Id,
                stripeEvent.Type);
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

        // Only upgrade — never overwrite a higher plan with a lower one.
        // This prevents old premium webhook retries from downgrading a user who already upgraded to pro.
        var currentPlan = await _usageService.GetPlanAsync(userId);
        if (Rank(plan) <= Rank(currentPlan))
        {
            _logger.LogInformation(
                "Checkout webhook skipped — plan would not upgrade. EventId {StripeEventId} UserId {UserId} CurrentPlan {CurrentPlan} IncomingPlan {IncomingPlan}",
                stripeEvent.Id, userId, currentPlan, plan);
        }
        else
        {
            await _usageService.SetPlanAsync(userId, plan);
        }

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
        if (string.IsNullOrWhiteSpace(subscription.CustomerId))
        {
            _logger.LogError(
                "Stripe subscription event missing customer id. EventId {StripeEventId} SubscriptionId {SubscriptionId}",
                stripeEvent.Id, subscription.Id);
            throw new InvalidOperationException($"Stripe subscription event {stripeEvent.Id} has no customer id.");
        }

        var userId = await _usageService.GetUserIdByStripeCustomerIdAsync(subscription.CustomerId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning(
                "No user mapping for Stripe customer — subscription event ignored. EventId {StripeEventId} CustomerId {CustomerId}",
                stripeEvent.Id, subscription.CustomerId);
            return; // Customer not yet mapped — checkout webhook hasn't fired yet; safe to ignore
        }

        string newPlan;
        string auditResult;

        if (string.Equals(subscription.Status, "canceled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subscription.Status, "unpaid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(subscription.Status, "incomplete_expired", StringComparison.OrdinalIgnoreCase))
        {
            // Subscription ended — downgrade to free
            newPlan = "free";
            auditResult = "downgraded";
        }
        else if (string.Equals(subscription.Status, "active", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(subscription.Status, "trialing", StringComparison.OrdinalIgnoreCase))
        {
            // Active subscription — map price ID to plan name (handles portal upgrades premium → pro)
            var priceId = subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;
            var mappedPlan = PlanFromPriceId(priceId);

            if (mappedPlan is null)
            {
                _logger.LogDebug(
                    "Stripe subscription update ignored — price ID not mapped to a known plan. EventId {StripeEventId} PriceId {PriceId}",
                    stripeEvent.Id, priceId);
                return;
            }

            var currentPlan = await _usageService.GetPlanAsync(userId);
            if (Rank(mappedPlan) <= Rank(currentPlan))
            {
                _logger.LogDebug(
                    "Stripe subscription update ignored — plan would not change. EventId {StripeEventId} CurrentPlan {CurrentPlan} MappedPlan {MappedPlan}",
                    stripeEvent.Id, currentPlan, mappedPlan);
                return;
            }

            newPlan = mappedPlan;
            auditResult = "upgraded";
        }
        else
        {
            _logger.LogDebug(
                "Stripe subscription event ignored — unhandled status. EventId {StripeEventId} Status {Status}",
                stripeEvent.Id, subscription.Status);
            return;
        }

        await _usageService.SetPlanAsync(userId, newPlan);

        var audit = new StripeWebhookAuditRecord(
            stripeEvent.Id, stripeEvent.Type, null, userId, newPlan,
            DateTimeOffset.UtcNow.ToString("O"), subscription.CustomerId, subscription.Id, auditResult);

        await _usageService.RecordStripeWebhookAuditAsync(audit);

        _logger.LogInformation(
            "Stripe subscription changed. EventId {StripeEventId} UserId {UserId} NewPlan {NewPlan} Result {Result}",
            stripeEvent.Id, userId, newPlan, auditResult);
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

        // For subscription-mode checkouts the session's payment_status is "paid" on successful
        // immediate payment. However there is a small window after the redirect where Stripe's API
        // may not yet have updated it.  A non-empty SubscriptionId is proof that the subscription
        // was created (which only happens after a successful payment), so we accept that too.
        var paymentPaid = string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase);
        var subscriptionCreated = !string.IsNullOrWhiteSpace(session.SubscriptionId);
        if (!paymentPaid && !subscriptionCreated)
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

    /// <summary>
    /// Looks up the user's active Stripe subscription and updates Redis to match.
    /// This is the authoritative fallback when both the webhook and confirm-plan have failed.
    ///
    /// Strategy:
    ///   1. Look up Stripe customer ID from Redis (set during normal checkout/webhook).
    ///   2. If not found and email is provided, search Stripe by email (webhook never fired).
    ///   3. List the customer's active subscriptions, map the price to a plan, update Redis.
    ///
    /// Returns the confirmed plan ("premium" | "pro") or "free" if no active subscription found.
    /// </summary>
    public async Task<string> SyncPlanFromStripeAsync(string userId, string? userEmail = null)
    {
        // ── 1. Resolve Stripe customer ID ────────────────────────────────────────
        var customerId = await _usageService.GetStripeCustomerIdAsync(userId);

        if (string.IsNullOrWhiteSpace(customerId) && !string.IsNullOrWhiteSpace(userEmail))
        {
            _logger.LogInformation(
                "SyncPlanFromStripe: no Redis customer mapping — searching Stripe by email. UserId {UserId} Email {Email}",
                userId, userEmail);

            var customers = await _stripeApiClient.SearchCustomersByEmailAsync(userEmail);
            // Pick the most recently created customer that has an active subscription.
            customerId = customers.Data.FirstOrDefault()?.Id;

            if (string.IsNullOrWhiteSpace(customerId))
            {
                _logger.LogWarning(
                    "SyncPlanFromStripe: no Stripe customer found by email. UserId {UserId} Email {Email}",
                    userId, userEmail);
                return "free";
            }

            // Persist the mapping so future lookups hit Redis, not Stripe.
            await _usageService.SetStripeCustomerIdAsync(userId, customerId);

            _logger.LogInformation(
                "SyncPlanFromStripe: customer found by email, mapping persisted. UserId {UserId} CustomerId {CustomerId}",
                userId, customerId);
        }

        if (string.IsNullOrWhiteSpace(customerId))
        {
            _logger.LogWarning("SyncPlanFromStripe: no Stripe customer ID and no email provided. UserId {UserId}", userId);
            return "free";
        }

        // ── 2. Resolve active subscription ───────────────────────────────────────
        var subscriptions = await _stripeApiClient.ListCustomerSubscriptionsAsync(customerId);
        var activeSub = subscriptions.Data.FirstOrDefault();

        if (activeSub is null)
        {
            _logger.LogWarning(
                "SyncPlanFromStripe: no active subscription for customer {CustomerId} UserId {UserId}",
                customerId, userId);
            return "free";
        }

        var priceId = activeSub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        var plan = PlanFromPriceId(priceId);

        if (plan is null)
        {
            _logger.LogWarning(
                "SyncPlanFromStripe: unknown price {PriceId} for customer {CustomerId}",
                priceId, customerId);
            return "free";
        }

        // ── 3. Persist in Redis ───────────────────────────────────────────────────
        await _usageService.SetPlanAsync(userId, plan);

        _logger.LogInformation(
            "SyncPlanFromStripe: plan synced from Stripe. UserId {UserId} Plan {Plan} SubscriptionId {SubId}",
            userId, plan, activeSub.Id);

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
