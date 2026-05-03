using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/applications")]
[EnableRateLimiting("applications")]
public class ApplicationsController(IAppUserContext userContext, IApplicationService applications) : ControllerBase
{
    private void SetJobApplicationsStorageHeaders()
    {
        var info = applications.GetBackendInfo();
        Response.Headers["X-Job-Applications-Effective-Storage"] = info.EffectiveStorage;
        Response.Headers["X-Job-Applications-Configured-Storage"] = info.ConfiguredJobApplicationsStorage;
        if (info.Degraded)
        {
            Response.Headers["X-Job-Applications-Degraded"] = "true";
            if (!string.IsNullOrEmpty(info.DegradedReason))
                Response.Headers["X-Job-Applications-Degraded-Reason"] = info.DegradedReason;
        }
    }

    private bool RequireSignedIn(out string userId)
    {
        userId = userContext.UserId;
        return !userContext.IsAnonymous && !string.IsNullOrWhiteSpace(userId);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var rows = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(rows);
    }

    public sealed record CreateApplicationBody(
        [Required, StringLength(300, MinimumLength = 1)] string JobTitle,
        [Required, StringLength(300, MinimumLength = 1)] string Company,
        [StringLength(2000)] string? JobUrl,
        [StringLength(24_000)] string? JobDescription);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationBody body, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var list = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid().ToString("N")[..12];
        var doc = new JobApplicationDocument
        {
            Id = id,
            JobTitle = body.JobTitle.Trim(),
            Company = body.Company.Trim(),
            JobUrl = string.IsNullOrWhiteSpace(body.JobUrl) ? null : body.JobUrl.Trim(),
            JobDescription = body.JobDescription?.Trim(),
            Status = "draft",
            StatusUpdatedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Timeline =
            [
                new ApplicationTimelineEvent { Date = now, Description = "Bewerbung angelegt" },
            ],
        };
        list.Insert(0, doc);
        await applications.SaveAllAsync(userId, list, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(doc);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var doc = await applications.GetAsync(userId, id, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return doc is null ? NotFound() : Ok(doc);
    }

    public sealed record StatusBody(
        [Required, StringLength(80, MinimumLength = 1)] string Status,
        [StringLength(4000)] string? Note);

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] StatusBody body, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var list = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var doc = list.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (doc is null)
            return NotFound();

        doc.Status = string.IsNullOrWhiteSpace(body.Status) ? doc.Status : body.Status.Trim();
        doc.StatusUpdatedAt = DateTime.UtcNow;
        doc.UpdatedAt = DateTime.UtcNow;
        doc.Timeline.Insert(0,
            new ApplicationTimelineEvent
            {
                Date = DateTime.UtcNow,
                Description = $"Status: {doc.Status}",
                Note = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim(),
            });
        await applications.SaveAllAsync(userId, list, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(doc);
    }

    public sealed record TextBody([StringLength(200_000)] string? Text);

    [HttpPut("{id}/cover-letter")]
    public async Task<IActionResult> SaveCoverLetter(string id, [FromBody] TextBody body, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var list = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var doc = list.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (doc is null)
            return NotFound();

        doc.CoverLetterText = body.Text ?? string.Empty;
        doc.UpdatedAt = DateTime.UtcNow;
        await applications.SaveAllAsync(userId, list, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpPut("{id}/interview-notes")]
    public async Task<IActionResult> SaveInterviewNotes(string id, [FromBody] TextBody body, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var list = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var doc = list.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (doc is null)
            return NotFound();

        doc.InterviewNotes = body.Text ?? string.Empty;
        doc.UpdatedAt = DateTime.UtcNow;
        await applications.SaveAllAsync(userId, list, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(new { success = true });
    }

    public sealed record LinkSessionBody(
        [Required, StringLength(40, MinimumLength = 1)] string SessionType,
        [Required, StringLength(80, MinimumLength = 1)] string SessionId);

    [HttpPut("{id}/link-session")]
    public async Task<IActionResult> LinkSession(string id, [FromBody] LinkSessionBody body, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var list = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var doc = list.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
        if (doc is null)
            return NotFound();

        var st = (body.SessionType ?? string.Empty).Trim().ToLowerInvariant();
        if (st is "analysis" or "jobanalyzer")
            doc.AnalysisSessionId = body.SessionId;
        else if (st is "interview" or "interviewprep")
            doc.InterviewSessionId = body.SessionId;
        else
            return BadRequest(new { error = "invalid_session_type" });

        doc.UpdatedAt = DateTime.UtcNow;
        await applications.SaveAllAsync(userId, list, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!RequireSignedIn(out var userId))
            return Unauthorized();

        var list = await applications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var next = list.Where(a => !string.Equals(a.Id, id, StringComparison.Ordinal)).ToList();
        if (next.Count == list.Count)
            return NotFound();

        await applications.SaveAllAsync(userId, next, cancellationToken).ConfigureAwait(false);
        SetJobApplicationsStorageHeaders();
        return Ok(new { success = true });
    }
}
