using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/profile")]
public class ProfileController(
    CareerProfileService profileService,
    ClerkAuthService clerkAuth,
    ILlmSingleCompletionService llmSingleCompletion,
    CvParsingService cvParsingService,
    ILogger<ProfileController> logger) : ControllerBase
{
    private void SetCareerProfileStorageHeaders()
    {
        var info = profileService.GetBackendInfo();
        Response.Headers["X-Career-Profile-Effective-Storage"] = info.EffectiveStorage;
        Response.Headers["X-Career-Profile-Configured-Storage"] = info.ConfiguredCareerProfileStorage;
        if (info.Degraded)
        {
            Response.Headers["X-Career-Profile-Degraded"] = "true";
            if (!string.IsNullOrEmpty(info.DegradedReason))
                Response.Headers["X-Career-Profile-Degraded-Reason"] = info.DegradedReason;
        }
    }

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
    [EnableRateLimiting("agent_read")]
    public async Task<IActionResult> GetProfile()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var profile = await profileService.GetProfile(userId);
            SetCareerProfileStorageHeaders();
            return Ok(profile ?? new CareerProfile { UserId = userId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GET /api/profile failed for user {UserId}", userId);
            return StatusCode(503, new
            {
                error = "profile_read_failed",
                message = "Could not load profile from storage. Please retry.",
            });
        }
    }

    [HttpPost("onboarding")]
    [EnableRateLimiting("profile_writes")]
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
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    /// <summary>Onboarding überspringen — markiert das Profil als abgeschlossen ohne Pflichtdaten.</summary>
    [HttpPost("onboarding/skip")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> SkipOnboarding()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SkipOnboardingAsync(userId);
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpPut("skills")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> UpdateSkills([FromBody] UpdateSkillsRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.SetSkills(userId, request.Skills ?? new List<string>());
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    /// <summary>
    /// Erzeugt einen anonymisierten Profil-Fließtext für KI-Kontext (keine Namen/Adressen/Kontaktdaten im Output).
    /// </summary>
    [HttpPost("cv/anonymous-summary")]
    [EnableRateLimiting("cv_summary")]
    public async Task<IActionResult> AnonymousCvSummary([FromBody] AnonymousCvSummaryRequest? request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var lang = request?.Language?.Trim().ToLowerInvariant();
        var isEnglish = lang is "en" or "english";

        CareerProfile? profile;
        try
        {
            profile = await profileService.GetProfile(userId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Anonymous CV summary: profile load failed for {UserId}", userId);
            return StatusCode(503, new
            {
                error = "profile_load_failed",
                message = "Profil konnte nicht geladen werden. Bitte später erneut versuchen.",
            });
        }

        profile ??= new CareerProfile { UserId = userId };
        NormalizeProfileLists(profile);

        if (!HasEnoughForAnonymousSummary(profile))
        {
            return BadRequest(new
            {
                error = "insufficient_data",
                message = "Trage mindestens Skills, eine Berufserfahrung oder einen CV-Text (mind. 50 Zeichen) ein.",
            });
        }

        var prompt = BuildAnonymousCvSummaryPrompt(profile, isEnglish);
        try
        {
            var text = await llmSingleCompletion
                .CompleteAsync(prompt, 720, HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return StatusCode(502, new
                {
                    error = "llm_empty",
                    message = "Die KI hat keinen Text zurückgegeben.",
                });
            }

            var trimmed = text.Trim();
            if (trimmed.Length > CareerProfileStorageLimits.CvSummaryMaxChars)
                trimmed = trimmed[..CareerProfileStorageLimits.CvSummaryMaxChars];

            return Ok(new AnonymousCvSummaryResponse(trimmed));
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { error = "cancelled", message = "Anfrage abgebrochen." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Anonymous CV summary LLM failed for user {UserId}", userId);
            return StatusCode(502, new
            {
                error = "llm_failed",
                message = "Zusammenfassung konnte nicht erstellt werden. Bitte später erneut versuchen.",
            });
        }
    }

    private static bool HasEnoughForAnonymousSummary(CareerProfile p)
    {
        if ((p.Skills?.Count ?? 0) > 0)
            return true;
        if ((p.Experience?.Count ?? 0) > 0)
            return true;
        var cv = p.CvRawText?.Trim();
        return cv is { Length: >= 50 };
    }

    private static string BuildAnonymousCvSummaryPrompt(CareerProfile p, bool isEnglish)
    {
        var sb = new StringBuilder();
        if (isEnglish)
        {
            sb.AppendLine("You write a factual English prose summary (5–12 sentences) for an AI assistant user profile.");
            sb.AppendLine("STRICT: No person names, no postal addresses, no phone numbers, no email addresses, no URLs, no company names.");
            sb.AppendLine("Use neutral wording such as \"a mid-sized software company\" instead of specific employers.");
            sb.AppendLine("Describe work experience without naming specific organizations.");
            sb.AppendLine();
            sb.AppendLine("Structured input (may contain sensitive fields — do NOT copy literally into the output):");
        }
        else
        {
            sb.AppendLine("Du erstellst einen sachlichen deutschen Fließtext (5–12 Sätze) für ein KI-Assistenz-Profil.");
            sb.AppendLine("STRIKT: Keine Personennamen, keine Adressen, keine Telefonnummern, keine E-Mail-Adressen, keine URLs, keine Firmennamen.");
            sb.AppendLine("Nutze neutrale Formulierungen wie \"eine mittelständische Softwarefirma\" statt konkreter Unternehmen.");
            sb.AppendLine("Beschreibe Berufserfahrung ohne konkrete Arbeitgeber.");
            sb.AppendLine();
            sb.AppendLine("Strukturierte Input-Daten (ggf. mit sensiblen Spalten — NICHT wörtlich übernehmen):");
        }

        if (!string.IsNullOrWhiteSpace(p.FieldLabel))
            sb.AppendLine($"Berufsfeld: {p.FieldLabel}");
        if (!string.IsNullOrWhiteSpace(p.LevelLabel))
            sb.AppendLine($"Erfahrungslevel: {p.LevelLabel}");
        if (!string.IsNullOrWhiteSpace(p.CurrentRole))
            sb.AppendLine($"Aktuelle Rolle (Text kann Namen enthalten — NICHT ausgeben): {Truncate(p.CurrentRole, 300)}");

        if (p.Goals.Count > 0)
            sb.AppendLine("Ziele: " + string.Join("; ", p.Goals.Take(12)));

        if (p.Skills.Count > 0)
            sb.AppendLine("Skills: " + string.Join(", ", p.Skills.Take(40)));

        if (p.Experience.Count > 0)
        {
            sb.AppendLine("Berufserfahrung (Unternehmen NICHT im Output nennen):");
            foreach (var e in p.Experience.Take(8))
            {
                sb.Append(" - ");
                if (!string.IsNullOrWhiteSpace(e.Title))
                    sb.Append($"{e.Title.Trim()}. ");
                if (!string.IsNullOrWhiteSpace(e.Duration))
                    sb.Append($"Zeitraum: {e.Duration.Trim()}. ");
                if (!string.IsNullOrWhiteSpace(e.Summary))
                    sb.Append(Truncate(e.Summary.Trim(), 400));
                sb.AppendLine();
            }
        }

        if (p.EducationEntries.Count > 0)
        {
            sb.AppendLine("Ausbildung (Institution nicht nennen wenn nicht nötig — evtl. \"Studium\" ohne Ortsnamen):");
            foreach (var ed in p.EducationEntries.Take(6))
            {
                sb.Append(" - ");
                if (!string.IsNullOrWhiteSpace(ed.Degree))
                    sb.Append(ed.Degree.Trim());
                if (!string.IsNullOrWhiteSpace(ed.Year))
                    sb.Append($" ({ed.Year.Trim()})");
                sb.AppendLine();
            }
        }

        if (p.Languages.Count > 0)
        {
            sb.AppendLine("Sprachen: "
                + string.Join(", ", p.Languages
                    .Where(l => !string.IsNullOrWhiteSpace(l.Name))
                    .Take(12)
                    .Select(l => string.IsNullOrWhiteSpace(l.Level) ? l.Name!.Trim() : $"{l.Name!.Trim()} ({l.Level.Trim()})")));
        }

        var rawCv = p.CvRawText?.Trim();
        if (!string.IsNullOrEmpty(rawCv))
            sb.AppendLine();
        sb.AppendLine(isEnglish
            ? "CV raw text (truncated to 6000 chars, may contain PII — do not output):"
            : "CV-Rohtext (auf 6000 Zeichen gekürzt, kann PII enthalten — nicht ausgeben):");
        sb.AppendLine(Truncate(rawCv ?? string.Empty, 6000));
        sb.AppendLine();
        sb.AppendLine(isEnglish
            ? "Answer with the prose only: no heading, no leading bullet or number."
            : "Antwort nur mit dem Fließtext, ohne Überschrift, ohne Aufzählungszeichen am Anfang.");
        return sb.ToString();
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    [HttpPost("cv")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> UploadCv([FromBody] UploadCvRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "CV-Text darf nicht leer sein." });

        await profileService.SetCvText(userId, request.Text);
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true, length = request.Text.Length });
    }

    /// <summary>
    /// PDF-CV hochladen: Text extrahieren, per KI strukturieren, Rohtext speichern — Vorschau-Daten ohne automatisches Profil-Merge.
    /// </summary>
    [HttpPost("cv/upload-pdf")]
    [EnableRateLimiting("profile_writes")]
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
                .ParseCvWithAi(rawText, p => llmSingleCompletion.CompleteAsync(p, 800, HttpContext.RequestAborted))
                .ConfigureAwait(false);

            await profileService.SetCvText(userId, rawText).ConfigureAwait(false);

            SetCareerProfileStorageHeaders();
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
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> AddTargetJob([FromBody] AddTargetJobRequest request)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        try
        {
            var jobId = await profileService.AddTargetJob(userId, request.Title, request.Company, request.Description);
            SetCareerProfileStorageHeaders();
            return Ok(new { success = true, id = jobId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("target-jobs/{jobId}")]
    [EnableRateLimiting("profile_writes")]
    public async Task<IActionResult> RemoveTargetJob(string jobId)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await profileService.RemoveTargetJob(userId, jobId);
        SetCareerProfileStorageHeaders();
        return Ok(new { success = true });
    }

    [HttpPut]
    [EnableRateLimiting("profile_writes")]
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
            SetCareerProfileStorageHeaders();
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
    [Required]
    [StringLength(80)]
    public string Field { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string FieldLabel { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string Level { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    public string LevelLabel { get; set; } = string.Empty;

    [StringLength(200)]
    public string? CurrentRole { get; set; }

    [MaxLength(30)]
    public List<string>? Goals { get; set; }
}

public class UpdateSkillsRequest
{
    [MaxLength(30)]
    public List<string>? Skills { get; set; }
}

public class UploadCvRequest
{
    [Required]
    [StringLength(500_000)]
    public string Text { get; set; } = string.Empty;
}

public class UploadCvPdfRequest
{
    [Required]
    [StringLength(5_000_000)]
    public string Base64Pdf { get; set; } = string.Empty;
}

public class AddTargetJobRequest
{
    [Required]
    [StringLength(300)]
    public string Title { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Company { get; set; }

    [StringLength(12_000)]
    public string? Description { get; set; }
}
