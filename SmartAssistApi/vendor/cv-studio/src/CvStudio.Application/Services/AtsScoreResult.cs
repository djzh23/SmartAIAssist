namespace CvStudio.Application.Services;

public sealed class AtsScoreResult
{
    public JobCategory DetectedCategory { get; set; } = JobCategory.Allgemein;
    public int Score { get; set; }
    public int HardRequirementsScore { get; set; } // 0-30
    public int KeywordScore { get; set; } // 0-25
    public int EvidenceScore { get; set; } // 0-20
    public int CompletenessScore { get; set; } // 0-10
    public int FormattingScore { get; set; } // 0-10
    public int LanguageScore { get; set; } // 0-5
    public List<string> MatchedKeywords { get; set; } = [];
    public List<string> MatchedSkillKeywords { get; set; } = [];
    public List<string> MissingKeywords { get; set; } = [];
    public List<string> MissingMustHaveKeywords { get; set; } = [];
    public List<AtsImprovement> Improvements { get; set; } = [];

    public string ScoreLabel => Score switch
    {
        >= 85 => "Sehr gut",
        >= 70 => "Gut",
        >= 55 => "Ausreichend",
        _ => "Verbesserungsbedarf"
    };

    public string ScoreColor => Score switch
    {
        >= 85 => "#16A34A",
        >= 70 => "#D97706",
        >= 55 => "#EA580C",
        _ => "#C9392B"
    };

    public string CategoryLabel => DetectedCategory switch
    {
        JobCategory.SoftwareEntwickler => "💻 Software Entwickler",
        JobCategory.ItSupport => "🖥 IT Support",
        _ => "📦 Allgemein / Service"
    };
}
