using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController(
    IAgentService agentService,
    UsageService usageService,
    ClerkAuthService clerkAuthService,
    ILogger<AgentController> logger) : ControllerBase
{
    [HttpPost("ask")]
    public async Task<ActionResult<AgentResponse>> Ask([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "Message must not be empty." });

        if (request.Message.Length > 4000)
            return BadRequest(new { error = $"Message must not exceed 4000 characters (received {request.Message.Length})." });

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);

        if (userId is null)
            return Unauthorized(new { error = "Invalid authentication token." });

        var usageCheck = await usageService.CheckAndIncrementAsync(userId, isAnonymous);

        if (!usageCheck.Allowed)
        {
            return StatusCode(429, new
            {
                error      = "usage_limit_reached",
                reason     = usageCheck.Reason,
                message    = usageCheck.Message,
                usageToday = usageCheck.UsageToday,
                dailyLimit = usageCheck.DailyLimit,
                plan       = usageCheck.Plan,
            });
        }

        Response.Headers.Append("X-Usage-Today",  usageCheck.UsageToday.ToString());
        Response.Headers.Append("X-Usage-Limit",  usageCheck.DailyLimit == int.MaxValue ? "∞" : usageCheck.DailyLimit.ToString());
        Response.Headers.Append("X-Usage-Plan",   usageCheck.Plan);

        try
        {
            var result = await agentService.RunAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fehler im AgentService");
            return StatusCode(500, new { error = ex.Message, details = ex.InnerException?.Message });
        }
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage()
    {
        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (userId is null) return Unauthorized();

        var lookupKey = isAnonymous ? $"anon:{userId}" : userId;
        var plan      = isAnonymous ? "anonymous" : await usageService.GetPlanAsync(userId);
        var usage     = await usageService.GetUsageTodayAsync(lookupKey);
        var limit     = UsageService.GetDailyLimit(plan);

        return Ok(new
        {
            plan,
            usageToday    = usage,
            dailyLimit    = limit == int.MaxValue ? (int?)null : limit,
            responsesLeft = limit == int.MaxValue ? (int?)null : Math.Max(0, limit - usage),
            isAnonymous,
            resetsAt      = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });
}
