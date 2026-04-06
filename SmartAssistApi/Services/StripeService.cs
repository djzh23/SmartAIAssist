using Stripe;
using Stripe.Checkout;

namespace SmartAssistApi.Services;

public class StripeService
{
    private readonly IConfiguration _config;
    private readonly UsageService _usageService;

    public StripeService(IConfiguration config, UsageService usageService)
    {
        _config = config;
        _usageService = usageService;
        StripeConfiguration.ApiKey = config["Stripe:SecretKey"]
            ?? throw new InvalidOperationException("Stripe:SecretKey missing");
    }

    public async Task<string> CreateCheckoutSessionAsync(string userId, string userEmail, string plan)
    {
        var priceId = plan == "premium"
            ? _config["Stripe:PremiumPriceId"] ?? _config["Stripe:PremiumPriceEid"]
            : _config["Stripe:ProPriceId"] ?? _config["Stripe:ProPriceEid"];

        if (string.IsNullOrEmpty(priceId))
            throw new InvalidOperationException($"Price ID for {plan} not configured");

        var frontendBaseUrl = _config["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl missing");

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
            Metadata = new Dictionary<string, string>
            {
                ["userId"] = userId,
                ["plan"] = plan
            },
            CustomerEmail = userEmail,
            SuccessUrl = $"{frontendBaseUrl}/profile?upgraded=true",
            CancelUrl = $"{frontendBaseUrl}/pricing?cancelled=true",
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options);
        return session.Url;
    }

    public async Task<string> CreatePortalSessionAsync(string customerId)
    {
        var frontendBaseUrl = _config["Frontend:BaseUrl"]
            ?? throw new InvalidOperationException("Frontend:BaseUrl missing");

        var options = new Stripe.BillingPortal.SessionCreateOptions
        {
            Customer = customerId,
            ReturnUrl = $"{frontendBaseUrl}/profile"
        };

        var service = new Stripe.BillingPortal.SessionService();
        var session = await service.CreateAsync(options);
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
            throw new InvalidOperationException(
                $"Webhook signature verification failed: {ex.Message}");
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                if (stripeEvent.Data.Object is Stripe.Checkout.Session session)
                    await HandleCheckoutCompletedAsync(session);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
            case EventTypes.CustomerSubscriptionUpdated:
                if (stripeEvent.Data.Object is Subscription subscription)
                    await HandleSubscriptionChangedAsync(subscription);
                break;

            case EventTypes.InvoicePaid:
                if (stripeEvent.Data.Object is Invoice invoice)
                    await HandleInvoicePaidAsync(invoice);
                break;
        }
    }

    private async Task HandleCheckoutCompletedAsync(Stripe.Checkout.Session session)
    {
        var userId = session.Metadata?.GetValueOrDefault("userId");
        var plan = session.Metadata?.GetValueOrDefault("plan");

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(plan))
            return;

        await _usageService.SetPlanAsync(userId, plan);

        if (!string.IsNullOrEmpty(session.CustomerId))
            await _usageService.SetStripeCustomerIdAsync(userId, session.CustomerId);
    }

    private async Task HandleSubscriptionChangedAsync(Subscription subscription)
    {
        if (!string.Equals(subscription.Status, "canceled", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrEmpty(subscription.CustomerId))
            return;

        var userId = await _usageService.GetUserIdByStripeCustomerIdAsync(subscription.CustomerId);
        if (!string.IsNullOrEmpty(userId))
            await _usageService.SetPlanAsync(userId, "free");
    }

    private static Task HandleInvoicePaidAsync(Invoice invoice)
    {
        _ = invoice;
        return Task.CompletedTask;
    }
}
