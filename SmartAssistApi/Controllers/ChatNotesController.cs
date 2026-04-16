using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/chat-notes")]
[EnableRateLimiting("sessions")]
public class ChatNotesController(ClerkAuthService clerkAuth, ChatNotesService chatNotes) : ControllerBase
{
    private void SetChatNotesStorageHeaders()
    {
        var info = chatNotes.GetBackendInfo();
        Response.Headers["X-Chat-Notes-Effective-Storage"] = info.EffectiveStorage;
        Response.Headers["X-Chat-Notes-Configured-Storage"] = info.ConfiguredChatNotesStorage;
        if (info.Degraded)
        {
            Response.Headers["X-Chat-Notes-Degraded"] = "true";
            if (!string.IsNullOrEmpty(info.DegradedReason))
                Response.Headers["X-Chat-Notes-Degraded-Reason"] = info.DegradedReason;
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

        var list = await chatNotes.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        SetChatNotesStorageHeaders();
        return Ok(list);
    }

    [HttpGet("{noteId}")]
    public async Task<IActionResult> Get(string noteId, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var note = await chatNotes.GetByIdAsync(userId, noteId, cancellationToken).ConfigureAwait(false);
        if (note is null)
            return NotFound();
        SetChatNotesStorageHeaders();
        return Ok(note);
    }

    public sealed record ChatNoteSourceDto(
        [StringLength(40)] string ToolType,
        [StringLength(80)] string SessionId,
        [StringLength(80)] string MessageId);

    public sealed record CreateChatNoteBody(
        [Required, StringLength(200, MinimumLength = 1)] string Title,
        [Required, StringLength(50_000, MinimumLength = 1)] string Body,
        [MaxLength(40)] List<string>? Tags,
        ChatNoteSourceDto? Source);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChatNoteBody body, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(body.Title) || string.IsNullOrWhiteSpace(body.Body))
            return BadRequest(new { error = "title_and_body_required" });

        ChatNoteSource? source = null;
        if (body.Source is not null)
        {
            var tt = body.Source.ToolType.Trim();
            var sid = body.Source.SessionId.Trim();
            var mid = body.Source.MessageId.Trim();
            if (tt.Length == 0 || sid.Length == 0 || mid.Length == 0)
                return BadRequest(new { error = "source_incomplete" });
            if (tt.Length > 48 || sid.Length > 64 || mid.Length > 128)
                return BadRequest(new { error = "source_too_long" });
            source = new ChatNoteSource { ToolType = tt, SessionId = sid, MessageId = mid };
        }

        var created = await chatNotes
            .CreateAsync(userId, body.Title, body.Body, body.Tags ?? [], source, cancellationToken)
            .ConfigureAwait(false);
        SetChatNotesStorageHeaders();
        return Ok(created);
    }

    public sealed record UpdateChatNoteBody(string? Title, string? Body, List<string>? Tags);

    [HttpPut("{noteId}")]
    public async Task<IActionResult> Update(string noteId, [FromBody] UpdateChatNoteBody body, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        if (body.Title is not null && string.IsNullOrWhiteSpace(body.Title))
            return BadRequest(new { error = "title_empty" });
        if (body.Body is not null && string.IsNullOrWhiteSpace(body.Body))
            return BadRequest(new { error = "body_empty" });

        var updated = await chatNotes
            .UpdateAsync(userId, noteId, body.Title, body.Body, body.Tags, cancellationToken)
            .ConfigureAwait(false);
        if (updated is null)
            return NotFound();
        SetChatNotesStorageHeaders();
        return Ok(updated);
    }

    [HttpDelete("{noteId}")]
    public async Task<IActionResult> Delete(string noteId, CancellationToken cancellationToken)
    {
        var auth = clerkAuth.ExtractUserId(Request);
        if (!RequireSignedIn(auth, out var userId))
            return Unauthorized();

        var ok = await chatNotes.DeleteAsync(userId, noteId, cancellationToken).ConfigureAwait(false);
        if (!ok)
            return NotFound();
        SetChatNotesStorageHeaders();
        return NoContent();
    }
}
