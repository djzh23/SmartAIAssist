using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/stripe")]
public class StripeController(
    StripeService stripeService,
    UsageService usageService,
    ClerkAuthService clerkAuthService,
    ILogger<StripeController> logger) : ControllerBase
{
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
    {
        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);

        if (isAnonymous || userId is null)
            return Unauthorized("You must be logged in to upgrade.");

        var normalizedPlan = request.Plan.ToLowerInvariant();
        if (normalizedPlan != "premium" && normalizedPlan != "pro")
            return BadRequest("Invalid plan. Must be 'premium' or 'pro'.");

        try
        {
            var checkoutUrl = await stripeService.CreateCheckoutSessionAsync(
                userId,
                request.Email ?? string.Empty,
                normalizedPlan);

            return Ok(new { url = checkoutUrl });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating checkout session");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("portal")]
    public async Task<IActionResult> CreatePortal()
    {
        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);

        if (isAnonymous || userId is null)
            return Unauthorized("You must be logged in.");

        try
        {
            var customerId = await usageService.GetStripeCustomerIdAsync(userId);
            if (string.IsNullOrEmpty(customerId))
            {
                return BadRequest(new
                {
                    error = "No subscription found.",
                    message = "You don't have an active subscription to manage."
                });
            }

            var portalUrl = await stripeService.CreatePortalSessionAsync(customerId);
            return Ok(new { url = portalUrl });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating portal session");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var payload = await new StreamReader(Request.Body).ReadToEndAsync();
        var stripeSignature = Request.Headers["Stripe-Signature"].FirstOrDefault();

        if (string.IsNullOrEmpty(stripeSignature))
            return BadRequest("Missing Stripe-Signature header");

        try
        {
            await stripeService.HandleWebhookAsync(payload, stripeSignature);
            return Ok(new { received = true });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning("Webhook verification failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook processing error");
            return StatusCode(500);
        }
    }

    [HttpGet("plans")]
    public IActionResult GetPlans([FromServices] IConfiguration config)
    {
        return Ok(new
        {
            premium = new
            {
                priceId = config["Stripe:PremiumPriceId"] ?? config["Stripe:PremiumPriceEid"],
                price = "4.99",
                currency = "eur",
                interval = "month"
            },
            pro = new
            {
                priceId = config["Stripe:ProPriceId"] ?? config["Stripe:ProPriceEid"],
                price = "9.99",
                currency = "eur",
                interval = "month"
            }
        });
    }
}

public record CheckoutRequest(string Plan, string? Email);
