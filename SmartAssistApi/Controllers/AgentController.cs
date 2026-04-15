using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Configuration;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController(
    IAgentService agentService,
    ConversationService conversationService,
    ChatSessionService chatSessionService,
    UsageService usageService,
    ClerkAuthService clerkAuthService,
    TokenTrackingService tokenTrackingService,
    ISpeechService speechService,
    ILogger<AgentController> logger) : ControllerBase
{
    private async Task<(ActionResult? Error, AgentRequest Normalized)> TryNormalizeAgentRequestAsync(
        AgentRequest request,
        string scopeUserId,
        bool isAnonymous,
        bool enforcePlan)
    {
        var resolved = AgentToolResolution.TryResolve(request.ToolType);
        if (resolved is null)
        {
            return (BadRequest(new { error = "unknown_tool", message = "Unbekanntes Werkzeug." }), request);
        }

        var skill = resolved.Skill;
        if (!skill.IsEnabled)
        {
            return (StatusCode(403, new
            {
                error = "coming_soon",
                message = $"{skill.Name} ist bald verfügbar.",
            }), request);
        }

        if (enforcePlan)
        {
            var plan = isAnonymous ? "anonymous" : await usageService.GetPlanAsync(scopeUserId);
            if (!SkillRegistry.IsToolAccessible(plan, skill))
            {
                return (StatusCode(403, new
                {
                    error = "plan_required",
                    message = "Für dieses Werkzeug ist ein höherer Tarif nötig.",
                }), request);
            }
        }

        var normalized = request with
        {
            ToolType = resolved.ApiToolType,
            CareerProfileUserId = isAnonymous ? null : scopeUserId,
            ConversationScopeUserId = scopeUserId,
            JobApplicationId = isAnonymous ? null : request.JobApplicationId,
        };

        return (null, normalized);
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AgentResponse>> Ask([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "message_empty", message = "Message must not be empty." });

        if (request.Message.Length > 4000)
            return BadRequest(new { error = "message_too_long", message = $"Message must not exceed 4000 characters (received {request.Message.Length})." });

        if (string.IsNullOrWhiteSpace(request.SessionId))
            return BadRequest(new { error = "session_id_required", message = "SessionId must not be empty." });

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);

        if (userId is null)
            return Unauthorized(new { error = "auth_failed", message = "Invalid authentication token." });

        logger.LogInformation(
            "Ask request. UserId {UserId} IsAnonymous {IsAnonymous} SessionId {SessionId} ToolType {ToolType}",
            userId, isAnonymous, request.SessionId, request.ToolType ?? "general");

        UsageCheckResult usageCheck;
        try
        {
            usageCheck = await usageService.CheckAndIncrementAsync(userId, isAnonymous);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Usage check failed (storage). UserId {UserId} IsAnonymous {IsAnonymous} SessionId {SessionId}",
                userId, isAnonymous, request.SessionId);
            return StatusCode(503, new
            {
                error = "usage_check_failed",
                message = "Usage service temporarily unavailable. Please retry.",
            });
        }

        if (!usageCheck.Allowed)
        {
            logger.LogInformation(
                "Usage limit reached. UserId {UserId} Plan {Plan} UsageToday {UsageToday} DailyLimit {DailyLimit}",
                userId, usageCheck.Plan, usageCheck.UsageToday, usageCheck.DailyLimit);
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
            var (normErr, agentRequest) = await TryNormalizeAgentRequestAsync(request, userId, isAnonymous, true);
            if (normErr != null)
                return normErr;

            var result = await agentService.RunAsync(agentRequest);
            FireTokenTracking(userId, agentRequest.ToolType, result);
            if (!isAnonymous)
            {
                _ = NotifySessionAfterMessageSafeAsync(
                    userId!,
                    request.SessionId!,
                    request.Message ?? "",
                    HttpContext.RequestAborted);
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Agent execution failed. UserId {UserId} SessionId {SessionId} ToolType {ToolType}",
                userId, request.SessionId, request.ToolType ?? "general");
            return StatusCode(500, new { error = "agent_error", message = "An internal error occurred. Please try again." });
        }
    }

    [HttpPost("stream")]
    public async Task AskStream([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "message_invalid", message = "Message must not be empty and must not exceed 4000 characters." }));
            return;
        }

        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            Response.StatusCode = 400;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "session_id_required", message = "SessionId must not be empty." }));
            return;
        }

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (userId is null)
        {
            Response.StatusCode = 401;
            await Response.WriteAsync(JsonSerializer.Serialize(new { error = "auth_failed", message = "Invalid authentication token." }));
            return;
        }

        logger.LogInformation(
            "Stream request. UserId {UserId} IsAnonymous {IsAnonymous} SessionId {SessionId} ToolType {ToolType}",
            userId, isAnonymous, request.SessionId, request.ToolType ?? "general");

        UsageCheckResult usageCheck;
        try
        {
            usageCheck = await usageService.CheckAndIncrementAsync(userId, isAnonymous);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Usage check failed (storage). UserId {UserId} IsAnonymous {IsAnonymous} SessionId {SessionId}",
                userId, isAnonymous, request.SessionId);
            Response.StatusCode = 503;
            await Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "usage_check_failed",
                message = "Usage service temporarily unavailable. Please retry.",
            }));
            return;
        }

        if (!usageCheck.Allowed)
        {
            logger.LogInformation(
                "Usage limit reached. UserId {UserId} Plan {Plan} UsageToday {UsageToday} DailyLimit {DailyLimit}",
                userId, usageCheck.Plan, usageCheck.UsageToday, usageCheck.DailyLimit);
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

        var (normErr, agentRequest) = await TryNormalizeAgentRequestAsync(request, userId, isAnonymous, true);
        if (normErr is not null)
        {
            var status = normErr switch
            {
                BadRequestObjectResult => 400,
                StatusCodeResult scr => scr.StatusCode,
                ObjectResult { StatusCode: { } c } => c,
                _ => 400,
            };
            Response.StatusCode = status;
            var payload = normErr is ObjectResult obr && obr.Value is not null
                ? obr.Value
                : new { error = "request_invalid" };
            await Response.WriteAsync(JsonSerializer.Serialize(payload));
            return;
        }

        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers["Cache-Control"]    = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        Response.Headers.Append("X-Usage-Today", usageCheck.UsageToday.ToString());
        Response.Headers.Append("X-Usage-Limit", usageCheck.DailyLimit == int.MaxValue ? "unlimited" : usageCheck.DailyLimit.ToString());
        Response.Headers.Append("X-Usage-Plan",  usageCheck.Plan);

        try
        {
            await foreach (var chunk in agentService.StreamAsync(agentRequest, HttpContext.RequestAborted))
            {
                string json;
                if (chunk.IsDone)
                {
                    FireTokenTracking(
                        userId,
                        agentRequest.ToolType,
                        chunk.InputTokens,
                        chunk.OutputTokens,
                        chunk.Model,
                        chunk.CacheCreationInputTokens,
                        chunk.CacheReadInputTokens);
                    if (!isAnonymous)
                    {
                        _ = NotifySessionAfterMessageSafeAsync(
                            userId!,
                            request.SessionId!,
                            request.Message ?? "",
                            HttpContext.RequestAborted);
                    }

                    json = JsonSerializer.Serialize(new
                    {
                        type = "done",
                        toolUsed = chunk.ToolUsed ?? "",
                        inputTokens = chunk.InputTokens,
                        outputTokens = chunk.OutputTokens,
                        model = chunk.Model,
                        cacheCreationInputTokens = chunk.CacheCreationInputTokens,
                        cacheReadInputTokens = chunk.CacheReadInputTokens,
                    });
                }
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
            logger.LogError(ex,
                "Streaming error. UserId {UserId} SessionId {SessionId} ToolType {ToolType}",
                userId, request.SessionId, request.ToolType ?? "general");
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

    /// <summary>
    /// Public demo endpoint — no Clerk auth required. Uses its own per-IP daily counter
    /// (separate from regular anonymous quota) so demo users never see a 429 error.
    /// Limit: 5 requests per IP per day.
    /// </summary>
    [HttpPost("demo")]
    public async Task<ActionResult<AgentResponse>> Demo([FromBody] AgentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { error = "message_empty" });

        if (request.Message.Length > 4000)
            return BadRequest(new { error = "message_too_long" });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var demoUserId = $"demo_agent:{ip}";
        const int demoLimit = 10; // 5 tools × 2 messages each

        int currentUsage;
        try
        {
            currentUsage = await usageService.GetUsageTodayAsync(demoUserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Demo usage check failed for IP {IP}", ip);
            return StatusCode(503, new { error = "usage_check_failed" });
        }

        if (currentUsage >= demoLimit)
            return StatusCode(429, new
            {
                error = "demo_limit_reached",
                reason = "demo_limit",
                message = "Demo limit reached. Sign up for 20 free messages per day.",
            });

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? $"demo_{ip}_{DateTime.UtcNow:yyyyMMdd}"
            : request.SessionId;

        var demoRequest = request with { SessionId = sessionId };

        logger.LogInformation("Demo request. IP {IP} Usage {Usage}/{Limit} ToolType {ToolType}",
            ip, currentUsage + 1, demoLimit, request.ToolType ?? "general");

        try
        {
            var (normErr, normalizedDemo) = await TryNormalizeAgentRequestAsync(demoRequest, demoUserId, false, false);
            if (normErr is not null)
                return normErr;

            await usageService.IncrementUsageAsync(demoUserId);
            var result = await agentService.RunAsync(normalizedDemo);
            FireTokenTracking(demoUserId, normalizedDemo.ToolType, result);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo agent execution failed. IP {IP} ToolType {ToolType}", ip, request.ToolType);
            return StatusCode(500, new { error = "agent_error", message = "An internal error occurred. Please try again." });
        }
    }

    /// <summary>ElevenLabs TTS via same contract as <c>/api/speech/tts</c>; path alias for agent clients.</summary>
    [HttpPost("speak")]
    public async Task<IActionResult> Speak([FromBody] SpeechRequest request, CancellationToken cancellationToken)
    {
        var (_, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (isAnonymous)
            return Unauthorized(new { error = "auth_required", message = "You must be signed in to use audio." });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text must not be empty." });

        if (request.Text.Length > 1200)
            return BadRequest(new { error = $"Text must not exceed 1200 characters (received {request.Text.Length})." });

        if (string.IsNullOrWhiteSpace(request.LanguageCode))
            return BadRequest(new { error = "LanguageCode must not be empty." });

        try
        {
            var speech = await speechService.SynthesizeAsync(request, cancellationToken);
            return File(speech.Audio, speech.ContentType);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Speech configuration error (agent speak)");
            return StatusCode(500, new { error = "speech_config_error", message = "Speech service is not configured correctly." });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Speech provider request failed (agent speak)");
            return StatusCode(502, new { error = "speech_provider_error", message = "Speech provider returned an error." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Agent speak endpoint failed");
            return StatusCode(500, new { error = "speech_error", message = "An internal error occurred." });
        }
    }

    [HttpPost("context")]
    public async Task<IActionResult> SetContext([FromBody] SetContextRequest request)
    {
        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in to set context." });

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
                    userId,
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
                    userId,
                    request.SessionId,
                    toolType,
                    ctx => ctx.ProgrammingLanguage = request.ProgrammingLanguage);
            }

            return Ok(new { success = true, sessionId = request.SessionId, toolType });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update agent context. SessionId {SessionId} ToolType {ToolType}", request.SessionId, toolType);
            return StatusCode(500, new { error = "context_update_failed" });
        }
    }

    [HttpGet("context/{sessionId}/{toolType}")]
    public async Task<IActionResult> GetContext(string sessionId, string toolType)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return BadRequest(new { error = "SessionId required." });

        var (userId, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in to read context." });

        var normalizedToolType = string.IsNullOrWhiteSpace(toolType)
            ? "general"
            : toolType.ToLowerInvariant();

        try
        {
            var context = await conversationService.GetContextAsync(userId, sessionId, normalizedToolType);
            return Ok(new
            {
                sessionId = context.SessionId,
                toolType = context.ToolType,
                conversationLanguage = context.ConversationLanguage,
                hasJob = context.Job?.IsAnalyzed == true,
                jobTitle = context.Job?.JobTitle,
                companyName = context.Job?.CompanyName,
                hasCV = !string.IsNullOrWhiteSpace(context.UserCV),
                // userCV is intentionally omitted — never expose raw CV text over the API
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
            return StatusCode(500, new { error = "context_read_failed" });
        }
    }

    private async Task NotifySessionAfterMessageSafeAsync(
        string userId,
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            await chatSessionService
                .NotifyAfterAgentMessageAsync(userId, sessionId, userMessage, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chat session metadata update failed. UserId {UserId} SessionId {SessionId}", userId, sessionId);
        }
    }

    private void FireTokenTracking(string userId, string? toolTypeRaw, AgentResponse result)
    {
        var tool = string.IsNullOrWhiteSpace(toolTypeRaw) ? "general" : toolTypeRaw.ToLowerInvariant();
        if (result.InputTokens is not { } i || result.OutputTokens is not { } o)
            return;

        var cc = result.CacheCreationInputTokens ?? 0;
        var cr = result.CacheReadInputTokens ?? 0;
        if (i == 0 && o == 0 && cc == 0 && cr == 0)
            return;

        _ = tokenTrackingService.TrackUsageAsync(userId, tool, result.Model ?? "unknown", i, o, cc, cr);
    }

    private void FireTokenTracking(
        string userId,
        string? toolTypeRaw,
        int? inputTokens,
        int? outputTokens,
        string? model,
        int? cacheCreationInputTokens = null,
        int? cacheReadInputTokens = null)
    {
        var tool = string.IsNullOrWhiteSpace(toolTypeRaw) ? "general" : toolTypeRaw.ToLowerInvariant();
        if (inputTokens is not { } i || outputTokens is not { } o)
            return;

        var cc = cacheCreationInputTokens ?? 0;
        var cr = cacheReadInputTokens ?? 0;
        if (i == 0 && o == 0 && cc == 0 && cr == 0)
            return;

        _ = tokenTrackingService.TrackUsageAsync(userId, tool, model ?? "unknown", i, o, cc, cr);
    }
}

public record SetContextRequest(
    string? SessionId,
    string? ToolType,
    string? CVText,
    string? JobTitle,
    string? CompanyName,
    string? ProgrammingLanguage);
