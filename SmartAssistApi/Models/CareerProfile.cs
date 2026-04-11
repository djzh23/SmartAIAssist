namespace SmartAssistApi.Models;

/// <summary>
/// Das Karriereprofil eines Users. Wird in Redis als JSON gespeichert.
/// Kann schrittweise aufgebaut werden — alle Felder sind optional.
/// </summary>
public class CareerProfile
{
    public string UserId { get; set; } = string.Empty;

    // === BASIS-PROFIL (Onboarding Schritt 1+2) ===
    public string? Field { get; set; }
    public string? FieldLabel { get; set; }
    public string? Level { get; set; }
    public string? LevelLabel { get; set; }
    public string? CurrentRole { get; set; }
    public List<string> Goals { get; set; } = new();

    // === ERWEITERTES PROFIL ===
    public List<string> Skills { get; set; } = new();
    public List<WorkExperience> Experience { get; set; } = new();
    public List<Education> EducationEntries { get; set; } = new();
    public List<ProfileLanguageEntry> Languages { get; set; } = new();

    // === CV-DATEN ===
    public string? CvRawText { get; set; }
    public string? CvSummary { get; set; }
    public DateTime? CvUploadedAt { get; set; }

    // === WUNSCHSTELLEN (max 3) ===
    public List<TargetJob> TargetJobs { get; set; } = new();

    // === METADATEN ===
    public bool OnboardingCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class WorkExperience
{
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Duration { get; set; }
    public string? Summary { get; set; }
}

public class Education
{
    public string? Degree { get; set; }
    public string? Institution { get; set; }
    public string? Year { get; set; }
}

public class ProfileLanguageEntry
{
    public string? Name { get; set; }
    public string? Level { get; set; }
}

public class TargetJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string? Title { get; set; }
    public string? Company { get; set; }
    public string? Description { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request um den Profil-Kontext für einen Chat-Request zu steuern.
/// </summary>
public class ProfileContextToggles
{
    public bool IncludeBasicProfile { get; set; } = true;
    public bool IncludeSkills { get; set; } = true;
    public bool IncludeExperience { get; set; }
    public bool IncludeCv { get; set; }
    public string? ActiveTargetJobId { get; set; }
}
