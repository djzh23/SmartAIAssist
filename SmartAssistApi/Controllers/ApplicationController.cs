using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/applications")]
public sealed class ApplicationController(ClerkAuthService clerkAuth, ApplicationService applicationService) : ControllerBase
{
    private static bool Blocked(string? userId, bool isAnonymous) =>
        isAnonymous || string.IsNullOrWhiteSpace(userId);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        var apps = await applicationService.GetApplications(userId!, cancellationToken).ConfigureAwait(false);
        return Ok(apps);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOne(string id, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        var apps = await applicationService.GetApplications(userId!, cancellationToken).ConfigureAwait(false);
        var app = apps.FirstOrDefault(a => a.Id == id);
        return app is null ? NotFound(new { error = "not_found" }) : Ok(app);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        if (string.IsNullOrWhiteSpace(request.JobTitle) || string.IsNullOrWhiteSpace(request.Company))
            return BadRequest(new { error = "job_title_and_company_required" });

        try
        {
            var app = await applicationService
                .CreateApplication(
                    userId!,
                    request.JobTitle,
                    request.Company,
                    request.JobUrl,
                    request.JobDescription,
                    cancellationToken)
                .ConfigureAwait(false);
            return Ok(app);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] UpdateStatusRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        try
        {
            await applicationService
                .UpdateStatus(userId!, id, request.Status, request.Note, cancellationToken)
                .ConfigureAwait(false);
            return Ok(new { success = true });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/cover-letter")]
    public async Task<IActionResult> SaveCoverLetter(string id, [FromBody] SaveTextRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        await applicationService.SaveCoverLetter(userId!, id, request.Text, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }

    [HttpPut("{id}/interview-notes")]
    public async Task<IActionResult> SaveInterviewNotes(string id, [FromBody] SaveTextRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        await applicationService.SaveInterviewNotes(userId!, id, request.Text, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }

    [HttpPut("{id}/link-session")]
    public async Task<IActionResult> LinkSession(string id, [FromBody] LinkSessionRequest request, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        await applicationService
            .LinkChatSession(userId!, id, request.SessionType, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new { success = true });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (Blocked(userId, isAnonymous))
            return Unauthorized(new { error = "auth_required" });

        await applicationService.DeleteApplication(userId!, id, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }
}

public sealed class CreateApplicationRequest
{
    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? JobUrl { get; set; }
    public string? JobDescription { get; set; }
}

public sealed class UpdateStatusRequest
{
    public ApplicationStatus Status { get; set; }
    public string? Note { get; set; }
}

public sealed class SaveTextRequest
{
    public string Text { get; set; } = string.Empty;
}

public sealed class LinkSessionRequest
{
    public string SessionType { get; set; } = "";
    public string SessionId { get; set; } = "";
}
