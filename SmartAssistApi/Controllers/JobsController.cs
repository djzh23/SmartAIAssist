using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

/// <summary>Stellen-Vorschau für Chat-Setup (URL oder Rohtext).</summary>
[ApiController]
[Route("api/jobs")]
public class JobsController(
    ClerkAuthService clerkAuth,
    IJobContextExtractor jobContextExtractor,
    ILogger<JobsController> logger) : ControllerBase
{
    public const int MaxInputLength = 12_000;

    [HttpPost("preview")]
    [EnableRateLimiting("job_preview")]
    public async Task<IActionResult> Preview([FromBody] JobPreviewRequest? request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required" });

        if (request is null || string.IsNullOrWhiteSpace(request.Input))
            return Ok(new JobPreviewResponse(false, null, null, null, null, null, null, "Bitte einen Link oder Stellentext angeben."));

        var input = request.Input.Trim();
        if (input.Length > MaxInputLength)
        {
            return Ok(new JobPreviewResponse(
                false, null, null, null, null, null, null,
                $"Eingabe zu lang (max. {MaxInputLength} Zeichen)."));
        }

        try
        {
            var jc = await jobContextExtractor.ExtractAsync(input).ConfigureAwait(false);
            return Ok(new JobPreviewResponse(
                true,
                jc.JobTitle,
                jc.CompanyName,
                jc.Location,
                jc.RawJobText,
                jc.KeyRequirements,
                jc.Keywords,
                null));
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Job preview validation failed");
            return Ok(new JobPreviewResponse(false, null, null, null, null, null, null, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Job preview extract failed");
            return Ok(new JobPreviewResponse(false, null, null, null, null, null, null, SanitizeUserMessage(ex.Message)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job preview unexpected failure");
            return Ok(new JobPreviewResponse(
                false, null, null, null, null, null, null,
                "Stellen-Vorschau fehlgeschlagen. Bitte den vollen Stellentext einfügen oder einen anderen Link versuchen."));
        }
    }

    private static string SanitizeUserMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Eingabe konnte nicht verarbeitet werden.";
        // ExtractAsync wraps URL errors in English — provide German hint for modal
        if (message.Contains("Could not load job posting URL", StringComparison.Ordinal))
            return "URL konnte nicht geladen werden. Kopiere die vollständige Stellenanzeige in den Tab „Text einfügen“.";
        if (message.Contains("too short", StringComparison.OrdinalIgnoreCase))
            return "Zu wenig Text — bitte die vollständige Stellenanzeige einfügen.";
        return message;
    }
}
