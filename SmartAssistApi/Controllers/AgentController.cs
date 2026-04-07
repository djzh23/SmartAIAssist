using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController(
    IAgentService agentService,
    ConversationService conversationService,
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
        Response.Headers.Append("X-Usage-Limit",  usageCheck.DailyLimit == int.MaxValue ? "unlimited" : usageCheck.DailyLimit.ToString());
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

    [HttpPost("stream")]
    public async Task AskStream([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "Invalid message." }));
            return;
        }

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (userId is null) { Response.StatusCode = 401; return; }

        var usageCheck = await usageService.CheckAndIncrementAsync(userId, isAnonymous);
        if (!usageCheck.Allowed)
        {
            Response.StatusCode = 429;
            await Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error      = "usage_limit_reached",
                reason     = usageCheck.Reason,
                usageToday = usageCheck.UsageToday,
                dailyLimit = usageCheck.DailyLimit,
                plan       = usageCheck.Plan,
            }));
            return;
        }

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"]    = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering
        Response.Headers.Append("X-Usage-Today", usageCheck.UsageToday.ToString());
        Response.Headers.Append("X-Usage-Limit", usageCheck.DailyLimit == int.MaxValue ? "unlimited" : usageCheck.DailyLimit.ToString());
        Response.Headers.Append("X-Usage-Plan",  usageCheck.Plan);

        try
        {
            await foreach (var chunk in agentService.StreamAsync(request, HttpContext.RequestAborted))
            {
                string json;
                if (chunk.IsDone)
                    json = JsonSerializer.Serialize(new { type = "done", toolUsed = chunk.ToolUsed ?? "" });
                else
                    json = JsonSerializer.Serialize(new { type = "chunk", text = chunk.Text ?? "" });

                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync(HttpContext.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — normal
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Streaming error for user {UserId}", userId);
            var err = JsonSerializer.Serialize(new { type = "error", message = ex.Message });
            await Response.WriteAsync($"data: {err}\n\n");
            await Response.Body.FlushAsync();
        }
    }

    [HttpGet("usage")]
    public async Task<IActionResult> GetUsage()
    {
        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (userId is null) return Unauthorized();

        try
        {
            var lookupKey = isAnonymous ? $"anon:{userId}" : userId;
            var plan = isAnonymous ? "anonymous" : await usageService.GetPlanStrictAsync(userId);
            var usage = await usageService.GetUsageTodayStrictAsync(lookupKey);
            var limit = UsageService.GetDailyLimit(plan);

            logger.LogDebug(
                "Usage read. UserId {UserId} Plan {Plan} UsageToday {UsageToday} DailyLimit {DailyLimit} IsAnonymous {IsAnonymous}",
                userId,
                plan,
                usage,
                limit,
                isAnonymous);

            return Ok(new
            {
                plan,
                usageToday = usage,
                dailyLimit = limit,
                responsesLeft = limit == int.MaxValue ? int.MaxValue : Math.Max(0, limit - usage),
                isAnonymous,
                isUnlimited = limit == int.MaxValue,
                resetsAt = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ssZ"),
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read usage and plan for user {UserId}", userId);
            // 503 = temporary storage unavailability, not a code bug — lets the client retry
            return StatusCode(503, new
            {
                error = "usage_read_failed",
                message = "Failed to read usage and plan from storage. Please retry.",
                details = ex.Message,
            });
        }
    }

    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", timestamp = DateTime.UtcNow });

    [HttpPost("context")]
    public async Task<IActionResult> SetContext([FromBody] SetContextRequest request)
    {
        _ = clerkAuthService.ExtractUserId(Request);

        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(new { error = "SessionId required." });

        var toolType = string.IsNullOrWhiteSpace(request.ToolType)
            ? "general"
            : request.ToolType.ToLowerInvariant();

        try
        {
            if (request.CVText is not null || request.JobTitle is not null || request.CompanyName is not null)
            {
                await conversationService.UpdateContextAsync(
                    request.SessionId,
                    toolType,
                    ctx =>
                    {
                        if (request.CVText is not null)
                            ctx.UserCV = request.CVText;
                        if (request.JobTitle is not null)
                            ctx.InterviewJobTitle = request.JobTitle;
                        if (request.CompanyName is not null)
                            ctx.InterviewCompany = request.CompanyName;
                    });
            }

            if (request.ProgrammingLanguage is not null)
            {
                await conversationService.UpdateContextAsync(
                    request.SessionId,
                    toolType,
                    ctx => ctx.ProgrammingLanguage = request.ProgrammingLanguage);
            }

            return Ok(new { success = true, sessionId = request.SessionId, toolType });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update agent context. SessionId {SessionId} ToolType {ToolType}", request.SessionId, toolType);
            return StatusCode(500, new { error = "context_update_failed", message = ex.Message });
        }
    }

    [HttpGet("context/{sessionId}/{toolType}")]
    public async Task<IActionResult> GetContext(string sessionId, string toolType)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId required." });

        var normalizedToolType = string.IsNullOrWhiteSpace(toolType)
            ? "general"
            : toolType.ToLowerInvariant();

        try
        {
            var context = await conversationService.GetContextAsync(sessionId, normalizedToolType);
            return Ok(new
            {
                sessionId = context.SessionId,
                toolType = context.ToolType,
                conversationLanguage = context.ConversationLanguage,
                hasJob = context.Job?.IsAnalyzed == true,
                jobTitle = context.Job?.JobTitle,
                companyName = context.Job?.CompanyName,
                hasCV = !string.IsNullOrWhiteSpace(context.UserCV),
                userCV = context.UserCV,
                interviewJobTitle = context.InterviewJobTitle,
                interviewCompany = context.InterviewCompany,
                hasProgrammingLang = !string.IsNullOrWhiteSpace(context.ProgrammingLanguage),
                programmingLanguage = context.ProgrammingLanguage,
                userFacts = context.UserFacts,
                practisedQuestions = context.PractisedQuestions,
                lastActivity = context.LastActivity,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read agent context. SessionId {SessionId} ToolType {ToolType}", sessionId, normalizedToolType);
            return StatusCode(500, new { error = "context_read_failed", message = ex.Message });
        }
    }
}

public record SetContextRequest(
    string? SessionId,
    string? ToolType,
    string? CVText,
    string? JobTitle,
    string? CompanyName,
    string? ProgrammingLanguage);
