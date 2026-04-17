using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/sessions")]
[EnableRateLimiting("sessions")]
public class SessionsController(ClerkAuthService clerkAuth, ChatSessionService chatSessions) : ControllerBase
{
    private void SetChatSessionStorageHeaders()
    {
        var info = chatSessions.GetBackendInfo();
        Response.Headers["X-Chat-Sessions-Effective-Storage"] = info.EffectiveStorage;
        Response.Headers["X-Chat-Sessions-Configured-Storage"] = info.ConfiguredChatSessionStorage;
        if (info.Degraded)
        {
            Response.Headers["X-Chat-Sessions-Degraded"] = "true";
            if (!string.IsNullOrEmpty(info.DegradedReason))
                Response.Headers["X-Chat-Sessions-Degraded-Reason"] = info.DegradedReason;
        }
    }

    private static bool RequireSignedIn((string? userId, bool isAnonymous) auth, out string userId)
    {
        userId = auth.userId ?? string.Empty;
        return !auth.isAnonymous && !string.IsNullOrWhiteSpace(userId);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var rows = await chatSessions.LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        SetChatSessionStorageHeaders();
        return Ok(rows);
    }

    public sealed record CreateSessionBody(
        [StringLength(40)] string ToolType,
        [StringLength(120)] string? Title);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSessionBody body, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var id = Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.UtcNow;
        var tool = string.IsNullOrWhiteSpace(body.ToolType) ? "general" : body.ToolType.Trim();
        var row = new ChatSessionRecord
        {
            Id = id,
            Title = string.IsNullOrWhiteSpace(body.Title) ? "Neues Gespräch" : body.Title.Trim(),
            ToolType = tool,
            CreatedAt = now,
            LastMessageAt = now,
            MessageCount = 0,
        };

        var list = await chatSessions.LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        list.Insert(0, row);
        await chatSessions.SaveIndexAsync(userId, list, cancellationToken).ConfigureAwait(false);
        await chatSessions.SaveTranscriptAsync(userId, id, tool, "[]", cancellationToken).ConfigureAwait(false);
        SetChatSessionStorageHeaders();
        return Ok(row);
    }

    public sealed record OrderBody([MaxLength(200)] List<string> OrderedSessionIds);

    [HttpPut("order")]
    public async Task<IActionResult> SaveOrder([FromBody] OrderBody body, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var list = await chatSessions.LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        var wanted = body.OrderedSessionIds ?? new List<string>();
        var map = list.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var reordered = new List<ChatSessionRecord>();
        foreach (var id in wanted)
        {
            if (map.TryGetValue(id, out var row))
                reordered.Add(row);
        }

        foreach (var row in list)
        {
            if (!reordered.Any(r => r.Id == row.Id))
                reordered.Add(row);
        }

        await chatSessions.SaveIndexAsync(userId, reordered, cancellationToken).ConfigureAwait(false);
        SetChatSessionStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpGet("{sessionId}/transcript")]
    public async Task<IActionResult> GetTranscript(string sessionId, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var t = await chatSessions.GetTranscriptAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (t is null)
            return NotFound(new { error = "not_found" });

        JsonElement messages;
        try
        {
            messages = JsonSerializer.Deserialize<JsonElement>(t.Value.MessagesJson);
        }
        catch
        {
            messages = JsonSerializer.SerializeToElement(Array.Empty<object>());
        }

        SetChatSessionStorageHeaders();
        return Ok(new { toolType = t.Value.ToolType, messages });
    }

    public sealed record TranscriptPutBody(
        [StringLength(40)] string ToolType,
        object Messages);

    [HttpPut("{sessionId}/transcript")]
    public async Task<IActionResult> PutTranscript(string sessionId, [FromBody] TranscriptPutBody body, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var tool = string.IsNullOrWhiteSpace(body.ToolType) ? "general" : body.ToolType.Trim();
        var messagesJson = System.Text.Json.JsonSerializer.Serialize(body.Messages);
        await chatSessions.SaveTranscriptAsync(userId, sessionId, tool, messagesJson, cancellationToken).ConfigureAwait(false);
        SetChatSessionStorageHeaders();
        return Ok(new { success = true });
    }

    public sealed record PatchSessionTitleBody([StringLength(120)] string? Title);

    /// <summary>Rename a chat tab (session list title in the persisted index).</summary>
    [HttpPatch("{sessionId}")]
    public async Task<IActionResult> PatchTitle(string sessionId, [FromBody] PatchSessionTitleBody body, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var trimmed = (body.Title ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return BadRequest(new { error = "title_required" });
        if (trimmed.Length > 120)
            trimmed = trimmed[..120];

        var list = await chatSessions.LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        var row = list.FirstOrDefault(r => string.Equals(r.Id, sessionId, StringComparison.Ordinal));
        if (row is null)
            return NotFound(new { error = "not_found" });

        row.Title = trimmed;
        await chatSessions.SaveIndexAsync(userId, list, cancellationToken).ConfigureAwait(false);
        SetChatSessionStorageHeaders();
        return Ok(row);
    }

    [HttpDelete("{sessionId}")]
    public async Task<IActionResult> Delete(string sessionId, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var list = await chatSessions.LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        var next = list.Where(r => !string.Equals(r.Id, sessionId, StringComparison.Ordinal)).ToList();
        await chatSessions.SaveIndexAsync(userId, next, cancellationToken).ConfigureAwait(false);
        await chatSessions.DeleteTranscriptAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        SetChatSessionStorageHeaders();
        return Ok(new { success = true });
    }
}
