using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController(
    CareerProfileService profileService,
    ClerkAuthService clerkAuth,
    AgentService agentService,
    CvParsingService cvParsingService,
    ILogger<ProfileController> logger) : ControllerBase
{
    private static void NormalizeProfileLists(CareerProfile profile)
    {
        profile.Goals ??= new List<string>();
        profile.Skills ??= new List<string>();
        profile.Experience ??= new List<WorkExperience>();
        profile.EducationEntries ??= new List<Education>();
        profile.Languages ??= new List<ProfileLanguageEntry>();
        profile.TargetJobs ??= new List<TargetJob>();
    }

    [HttpGet]
    public async Task<IActionResult> GetProfile()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var profile = await profileService.GetProfile(userId);
        return Ok(profile ?? new CareerProfile { UserId = userId });
    }

    [HttpPost("onboarding")]
    public async Task<IActionResult> CompleteOnboarding([FromBody] OnboardingRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SetOnboarding(
            userId,
            request.Field,
            request.FieldLabel,
            request.Level,
            request.LevelLabel,
            request.CurrentRole,
            request.Goals ?? new List<string>());
        return Ok(new { success = true });
    }

    /// <summary>Onboarding überspringen — markiert das Profil als abgeschlossen ohne Pflichtdaten.</summary>
    [HttpPost("onboarding/skip")]
    public async Task<IActionResult> SkipOnboarding()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SkipOnboardingAsync(userId);
        return Ok(new { success = true });
    }

    [HttpPut("skills")]
    public async Task<IActionResult> UpdateSkills([FromBody] UpdateSkillsRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SetSkills(userId, request.Skills ?? new List<string>());
        return Ok(new { success = true });
    }

    [HttpPost("cv")]
    public async Task<IActionResult> UploadCv([FromBody] UploadCvRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "CV-Text darf nicht leer sein." });

        await profileService.SetCvText(userId, request.Text);
        return Ok(new { success = true, length = request.Text.Length });
    }

    /// <summary>
    /// PDF-CV hochladen: Text extrahieren, per KI strukturieren, Rohtext speichern — Vorschau-Daten ohne automatisches Profil-Merge.
    /// </summary>
    [HttpPost("cv/upload-pdf")]
    public async Task<IActionResult> UploadCvPdf([FromBody] UploadCvPdfRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrEmpty(request.Base64Pdf))
            return BadRequest(new { error = "PDF-Daten fehlen." });

        if (request.Base64Pdf.Length > 5_000_000)
            return BadRequest(new { error = "PDF darf maximal 5MB groß sein." });

        try
        {
            string rawText;
            try
            {
                rawText = cvParsingService.ExtractTextFromPdf(request.Base64Pdf);
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "Ungültige Base64-Daten." });
            }

            if (string.IsNullOrWhiteSpace(rawText) || rawText.Length < 50)
                return BadRequest(new { error = "Konnte keinen Text aus der PDF extrahieren. Ist es ein Bild-PDF?" });

            var parsed = await cvParsingService
                .ParseCvWithAi(rawText, p => agentService.SingleCompletion(p, 800))
                .ConfigureAwait(false);

            await profileService.SetCvText(userId, rawText).ConfigureAwait(false);

            return Ok(new
            {
                success = true,
                rawTextLength = rawText.Length,
                parsed,
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CV PDF upload failed for user {UserId}", userId);
            return BadRequest(new { error = $"PDF-Verarbeitung fehlgeschlagen: {ex.Message}" });
        }
    }

    [HttpPost("target-jobs")]
    public async Task<IActionResult> AddTargetJob([FromBody] AddTargetJobRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var jobId = await profileService.AddTargetJob(userId, request.Title, request.Company, request.Description);
            return Ok(new { success = true, id = jobId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("target-jobs/{jobId}")]
    public async Task<IActionResult> RemoveTargetJob(string jobId)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.RemoveTargetJob(userId, jobId);
        return Ok(new { success = true });
    }

    [HttpPut]
    public async Task<IActionResult> UpdateProfile([FromBody] CareerProfile profile)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        profile.UserId = userId;
        NormalizeProfileLists(profile);

        if (profile.TargetJobs.Count > 3)
            return BadRequest(new { error = "Maximal 3 Wunschstellen erlaubt." });
        if (profile.Skills.Count > 30)
            return BadRequest(new { error = "Maximal 30 Skills erlaubt." });

        try
        {
            await profileService.SaveProfile(userId, profile);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save career profile for user {UserId}", userId);
            return StatusCode(500, new { error = "profile_save_failed" });
        }
    }
}

public class OnboardingRequest
{
    public string Field { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string LevelLabel { get; set; } = string.Empty;
    public string? CurrentRole { get; set; }
    public List<string>? Goals { get; set; }
}

public class UpdateSkillsRequest
{
    public List<string>? Skills { get; set; }
}

public class UploadCvRequest
{
    public string Text { get; set; } = string.Empty;
}

public class UploadCvPdfRequest
{
    public string Base64Pdf { get; set; } = string.Empty;
}

public class AddTargetJobRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Description { get; set; }
}
