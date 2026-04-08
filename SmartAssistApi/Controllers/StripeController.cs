using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/stripe")]
public class StripeController(
    StripeService stripeService,
    UsageService usageService,
    ClerkAuthService clerkAuthService,
    IConfiguration config,
    ILogger<StripeController> logger) : ControllerBase
{
    [HttpPost("checkout")]
    public async Task<IActionResult> CreateCheckout([FromBody] CheckoutRequest request)
    {
        var (canonicalUserId, isAnonymous) = clerkAuthService.ExtractUserId(Request);

        if (isAnonymous || canonicalUserId is null)
            return Unauthorized("You must be logged in to upgrade.");

        if (!string.IsNullOrWhiteSpace(request.UserId)
            && !string.Equals(request.UserId, canonicalUserId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Checkout rejected due to userId mismatch. JwtUserId {JwtUserId} BodyUserId {BodyUserId}",
                canonicalUserId,
                request.UserId);

            return StatusCode(403, new { error = "user_mismatch", message = "Body userId does not match authenticated user." });
        }

        var normalizedPlan = request.Plan.ToLowerInvariant();
        if (normalizedPlan != "premium" && normalizedPlan != "pro")
            return BadRequest("Invalid plan. Must be 'premium' or 'pro'.");

        try
        {
            var correlationId = HttpContext.TraceIdentifier;
            var checkoutUrl = await stripeService.CreateCheckoutSessionAsync(
                canonicalUserId,
                request.Email ?? string.Empty,
                normalizedPlan,
                correlationId);

            return Ok(new { url = checkoutUrl, correlationId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error creating checkout session. UserId {UserId} Plan {Plan} CorrelationId {CorrelationId}",
                canonicalUserId,
                normalizedPlan,
                HttpContext.TraceIdentifier);

            return StatusCode(500, new { error = "checkout_failed" });
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
            logger.LogError(ex, "Error creating portal session. UserId {UserId}", userId);
            return StatusCode(500, new { error = "portal_failed" });
        }
    }

    [HttpGet("confirm-plan")]
    public async Task<IActionResult> ConfirmPlan([FromQuery] string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "session_id query parameter is required." });

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrWhiteSpace(userId))
            return Unauthorized("You must be logged in.");

        try
        {
            var confirmedPlan = await stripeService.ConfirmPlanFromSessionAsync(userId, sessionId);
            return Ok(new { plan = confirmedPlan, confirmed = true });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("does not belong"))
        {
            logger.LogWarning(ex, "Confirm-plan userId mismatch. UserId {UserId} SessionId {SessionId}", userId, sessionId);
            return StatusCode(403, new { error = "session_mismatch", message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Confirm-plan failed. UserId {UserId} SessionId {SessionId}", userId, sessionId);
            return StatusCode(500, new { error = "confirm_plan_failed" });
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
        catch (StripeWebhookSignatureException ex)
        {
            logger.LogWarning(ex, "Stripe webhook signature verification failed");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe webhook processing failed");
            return StatusCode(500, new
            {
                error = "webhook_processing_failed",
                message = ex.Message,
            });
        }
    }

    [HttpGet("plans")]
    public IActionResult GetPlans([FromServices] IConfiguration appConfig)
    {
        return Ok(new
        {
            premium = new
            {
                priceId = appConfig["Stripe:PremiumPriceId"],
                price = "4.99",
                currency = "eur",
                interval = "month"
            },
            pro = new
            {
                priceId = appConfig["Stripe:ProPriceId"],
                price = "9.99",
                currency = "eur",
                interval = "month"
            }
        });
    }

    [HttpGet("debug/me")]
    public async Task<IActionResult> DebugMe()
    {
        if (!config.GetValue("Stripe:EnableDebugEndpoint", false))
            return NotFound(new { error = "debug_endpoint_disabled" });

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrWhiteSpace(userId))
            return Unauthorized("You must be logged in.");

        try
        {
            var debugInfo = await usageService.GetStripeDebugInfoAsync(userId);
            return Ok(new
            {
                userId = debugInfo.UserId,
                currentStoredPlan = debugInfo.CurrentPlan,
                lastProcessedStripeEventId = debugInfo.LastStripeEventId,
                lastProcessedStripeEventAt = debugInfo.LastStripeEventAt,
                lastCheckoutSessionId = debugInfo.LastCheckoutSessionId,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Stripe debug endpoint failed for user {UserId}", userId);
            return StatusCode(500, new
            {
                error = "stripe_debug_failed",
                message = ex.Message,
            });
        }
    }
}

public record CheckoutRequest(string Plan, string? Email, string? UserId);
