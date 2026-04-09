using Stripe;
using Stripe.Checkout;

namespace SmartAssistApi.Services;

public interface IStripeApiClient
{
    Task<Session> CreateCheckoutSessionAsync(SessionCreateOptions options);
    Task<Session> GetCheckoutSessionAsync(string sessionId);
    Task<Stripe.BillingPortal.Session> CreateBillingPortalSessionAsync(Stripe.BillingPortal.SessionCreateOptions options);
    Task<StripeList<Subscription>> ListCustomerSubscriptionsAsync(string customerId);
    Task<StripeSearchResult<Customer>> SearchCustomersByEmailAsync(string email);
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

    public Task<StripeList<Subscription>> ListCustomerSubscriptionsAsync(string customerId)
    {
        var service = new SubscriptionService();
        return service.ListAsync(new SubscriptionListOptions
        {
            Customer = customerId,
            Status   = "active",
            Expand   = ["data.items.data.price"],
        });
    }

    public Task<StripeSearchResult<Customer>> SearchCustomersByEmailAsync(string email)
    {
        var service = new CustomerService();
        return service.SearchAsync(new CustomerSearchOptions
        {
            Query = $"email:'{email}'",
            Limit = 5,
        });
    }
}
