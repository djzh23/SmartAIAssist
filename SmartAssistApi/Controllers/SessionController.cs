using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/sessions")]
public sealed class SessionController(
    ClerkAuthService clerkAuth,
    ChatSessionService sessionService,
    ILogger<SessionController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static bool IsAnonymousOrMissing(string? userId, bool isAnonymous) =>
        isAnonymous || string.IsNullOrWhiteSpace(userId);

    [HttpGet]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        var sessions = await sessionService.GetSessions(userId!, cancellationToken).ConfigureAwait(false);
        return Ok(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        var session = await sessionService
            .CreateSession(userId!, request.ToolType, request.Title, cancellationToken)
            .ConfigureAwait(false);
        return Ok(session);
    }

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> DeleteSession(string sessionId, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        await sessionService.DeleteSession(userId!, sessionId, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }

    [HttpPut("{sessionId}/rename")]
    public async Task<IActionResult> RenameSession(string sessionId, [FromBody] RenameSessionRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "title_required" });

        await sessionService.RenameSession(userId!, sessionId, request.Title, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }

    [HttpPut("order")]
    public async Task<IActionResult> ReorderSessions([FromBody] ReorderSessionsRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        if (request.OrderedSessionIds is null || request.OrderedSessionIds.Count == 0)
            return BadRequest(new { error = "order_required" });

        try
        {
            await sessionService.ReorderSessions(userId!, request.OrderedSessionIds, cancellationToken)
                .ConfigureAwait(false);
            return Ok(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_order", message = ex.Message });
        }
    }

    [HttpGet("{sessionId}/transcript")]
    public async Task<IActionResult> GetTranscript(string sessionId, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        var meta = await sessionService.GetSession(userId!, sessionId, cancellationToken).ConfigureAwait(false);
        if (meta is null)
            return NotFound(new { error = "session_not_found" });

        var raw = await sessionService.GetTranscriptJson(userId!, sessionId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return Ok(new { toolType = meta.ToolType, messages = Array.Empty<object>() });

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("messages", out var messagesEl)
                && root.TryGetProperty("toolType", out var toolEl))
            {
                return Ok(new { toolType = toolEl.GetString() ?? meta.ToolType, messages = messagesEl });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Corrupt transcript for session {SessionId}", sessionId);
        }

        return Ok(new { toolType = meta.ToolType, messages = Array.Empty<object>() });
    }

    [HttpPut("{sessionId}/transcript")]
    public async Task<IActionResult> PutTranscript(string sessionId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (IsAnonymousOrMissing(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required", message = "You must be signed in." });

        var meta = await sessionService.GetSession(userId!, sessionId, cancellationToken).ConfigureAwait(false);
        if (meta is null)
            return NotFound(new { error = "session_not_found" });

        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "invalid_body" });

        if (!body.TryGetProperty("messages", out var messagesEl) || messagesEl.ValueKind != JsonValueKind.Array)
            return BadRequest(new { error = "messages_array_required" });

        var toolType = meta.ToolType;
        if (body.TryGetProperty("toolType", out var tt) && tt.ValueKind == JsonValueKind.String)
        {
            var parsed = tt.GetString();
            if (!string.IsNullOrWhiteSpace(parsed))
                toolType = parsed!;
        }

        var payload = JsonSerializer.SerializeToElement(new { toolType, messages = messagesEl }, JsonOpts);
        var json = payload.GetRawText();
        await sessionService.SaveTranscriptJson(userId!, sessionId, json, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }
}

public sealed class CreateSessionRequest
{
    public string ToolType { get; set; } = "general";
    public string? Title { get; set; }
}

public sealed class RenameSessionRequest
{
    public string Title { get; set; } = "";
}

public sealed class ReorderSessionsRequest
{
    public List<string> OrderedSessionIds { get; set; } = [];
}
