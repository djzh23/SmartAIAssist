using Stripe.Checkout;

namespace SmartAssistApi.Services;

public interface IStripeApiClient
{
    Task<Session> CreateCheckoutSessionAsync(SessionCreateOptions options);
    Task<Session> GetCheckoutSessionAsync(string sessionId);
    Task<Stripe.BillingPortal.Session> CreateBillingPortalSessionAsync(Stripe.BillingPortal.SessionCreateOptions options);
}

public sealed class StripeApiClient : IStripeApiClient
{
    public Task<Session> CreateCheckoutSessionAsync(SessionCreateOptions options)
    {
        var service = new SessionService();
        return service.CreateAsync(options);
    }

    public Task<Session> GetCheckoutSessionAsync(string sessionId)
    {
        var service = new SessionService();
        return service.GetAsync(sessionId);
    }

    public Task<Stripe.BillingPortal.Session> CreateBillingPortalSessionAsync(Stripe.BillingPortal.SessionCreateOptions options)
    {
        var service = new Stripe.BillingPortal.SessionService();
        return service.CreateAsync(options);
    }
}
