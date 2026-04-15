namespace SmartAssistApi.Configuration;

public class CareerSkill
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = string.Empty;
    public bool RequiresPaidPlan { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsBeta { get; set; }
    /// <summary>free, starter (maps to premium), pro — see <see cref="PlanTiers"/>.</summary>
    public string MinPlan { get; set; } = "free";
    public string[] ToolTypeAliases { get; set; } = Array.Empty<string>();
    /// <summary>Tool string used by <see cref="Services.SystemPromptBuilder"/> and agent pipeline.</summary>
    public string ApiToolType { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 600;
    public float Temperature { get; set; } = 0.5f;
}

public static class PlanTiers
{
    /// <summary>Order: higher = more entitlement. Anonymous is lowest.</summary>
    public static int Rank(string plan)
    {
        var p = (plan ?? "anonymous").ToLowerInvariant();
        return p switch
        {
            "anonymous" => 0,
            "free" => 1,
            "starter" => 2,
            "premium" => 2,
            "pro" => 3,
            _ => 0,
        };
    }

    public static bool MeetsMinPlan(string userPlan, string minPlan)
    {
        return Rank(userPlan) >= Rank(minPlan);
    }
}

public static class SkillRegistry
{
    public static readonly List<CareerSkill> AllSkills = BuildAll();

    private static List<CareerSkill> BuildAll() =>
    [
        new CareerSkill
        {
            Id = "job_analysis",
            Name = "Stellenanalyse",
            Description = "Analysiert Stellenanzeigen, findet Keywords und Lücken in deinem Profil",
            Icon = "briefcase",
            Category = "career",
            Badge = "Karriere",
            BadgeColor = "orange",
            MinPlan = "free",
            ToolTypeAliases = ["job", "stellenanalyse", "job_analysis", "jobanalyzer"],
            ApiToolType = "jobanalyzer",
            MaxTokens = 900,
            Temperature = 0.3f,
        },
        new CareerSkill
        {
            Id = "interview_coach",
            Name = "Interview Coach",
            Description = "Probe-Interviews mit Feedback und STAR-Antwortstruktur",
            Icon = "mic",
            Category = "career",
            Badge = "Karriere",
            BadgeColor = "orange",
            MinPlan = "free",
            ToolTypeAliases = ["interview", "vorstellungsgespräch", "interview_coach", "interviewprep"],
            ApiToolType = "interviewprep",
            MaxTokens = 1000,
            Temperature = 0.4f,
        },
        new CareerSkill
        {
            Id = "general_chat",
            Name = "Karriere-Chat",
            Description = "Allgemeine Karrierefragen, Texte verbessern, Ideen entwickeln",
            Icon = "message-circle",
            Category = "productivity",
            Badge = "Flex",
            BadgeColor = "gray",
            MinPlan = "free",
            ToolTypeAliases = ["general", "chat", "allgemein"],
            ApiToolType = "general",
            MaxTokens = 600,
            Temperature = 0.5f,
        },
        new CareerSkill
        {
            Id = "code_assistant",
            Name = "Code-Assistent",
            Description = "Code reviewen, Bugs finden, technische Interview-Vorbereitung",
            Icon = "code",
            Category = "productivity",
            Badge = "Tech",
            BadgeColor = "blue",
            MinPlan = "free",
            ToolTypeAliases = ["code", "programmierung", "programming"],
            ApiToolType = "programming",
            MaxTokens = 1000,
            Temperature = 0.2f,
        },
        new CareerSkill
        {
            Id = "language_training",
            Name = "Sprachtraining",
            Description = "Berufliches Vokabular und Bewerbungssprache in vielen Sprachen",
            Icon = "globe",
            Category = "learning",
            Badge = "Sprache",
            BadgeColor = "teal",
            MinPlan = "free",
            ToolTypeAliases = ["language", "sprachen", "sprachen lernen"],
            ApiToolType = "language",
            MaxTokens = 400,
            Temperature = 0.6f,
        },
        new CareerSkill
        {
            Id = "cover_letter",
            Name = "Anschreiben-Generator",
            Description = "Personalisiertes Anschreiben aus Profil und Stelle",
            Icon = "file-text",
            Category = "career",
            Badge = "Neu",
            BadgeColor = "teal",
            RequiresPaidPlan = true,
            MinPlan = "starter",
            IsEnabled = false,
            IsBeta = true,
            ToolTypeAliases = ["cover_letter", "anschreiben"],
            ApiToolType = "cover_letter",
            MaxTokens = 1200,
            Temperature = 0.4f,
        },
        new CareerSkill
        {
            Id = "salary_coach",
            Name = "Gehalts-Coach",
            Description = "Gehaltsverhandlung vorbereiten mit Formulierungen und Gegenargumenten",
            Icon = "trending-up",
            Category = "career",
            Badge = "Neu",
            BadgeColor = "teal",
            RequiresPaidPlan = true,
            MinPlan = "pro",
            IsEnabled = false,
            IsBeta = true,
            ToolTypeAliases = ["salary_coach", "gehalt"],
            ApiToolType = "salary_coach",
            MaxTokens = 800,
            Temperature = 0.4f,
        },
        new CareerSkill
        {
            Id = "linkedin_optimizer",
            Name = "LinkedIn-Optimierung",
            Description = "Headline, Summary und Erfahrungen für mehr Sichtbarkeit",
            Icon = "linkedin",
            Category = "career",
            Badge = "Bald",
            BadgeColor = "blue",
            RequiresPaidPlan = true,
            MinPlan = "pro",
            IsEnabled = false,
            IsBeta = true,
            ToolTypeAliases = ["linkedin", "linkedin_optimizer"],
            ApiToolType = "linkedin_optimizer",
            MaxTokens = 1000,
            Temperature = 0.5f,
        },
    ];

    public static CareerSkill? FindSkill(string? toolType)
    {
        var normalized = toolType?.Trim().ToLowerInvariant() ?? "";
        if (normalized.Length == 0)
            normalized = "general";

        return AllSkills.FirstOrDefault(s =>
            string.Equals(s.ApiToolType, normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Id, normalized, StringComparison.OrdinalIgnoreCase)
            || s.ToolTypeAliases.Any(a => a.Equals(normalized, StringComparison.OrdinalIgnoreCase)));
    }

    public static List<CareerSkill> GetVisibleSkills() =>
        AllSkills.Where(s => s.IsEnabled || s.IsBeta).ToList();

    public static bool IsToolAccessible(string plan, CareerSkill skill) =>
        !skill.RequiresPaidPlan || PlanTiers.MeetsMinPlan(plan, skill.MinPlan);
}
