using Microsoft.AspNetCore.Mvc;
using CvStudio.Application.DTOs;
using CvStudio.Application.Services;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/cv-studio/resume-templates")]
public sealed class CvStudioResumeTemplatesController(ClerkAuthService clerkAuth, IResumeService resumeService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResumeTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required" });

        var templates = await resumeService.ListTemplatesAsync(cancellationToken);
        return Ok(templates);
    }
}
