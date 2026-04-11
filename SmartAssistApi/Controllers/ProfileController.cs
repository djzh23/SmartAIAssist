using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController(
    CareerProfileService profileService,
    ClerkAuthService clerkAuth,
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

public class AddTargetJobRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Company { get; set; }
    public string? Description { get; set; }
}
