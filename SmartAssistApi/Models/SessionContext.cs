namespace SmartAssistApi.Models;

public class SessionContext
{
    public string SessionId { get; set; } = "";
    public string ToolType { get; set; } = "general";
    public string ConversationLanguage { get; set; } = "de";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public JobContext? Job { get; set; }
    public LanguageContext? Language { get; set; }
    public List<string> UserFacts { get; set; } = new();

    // Interview prep context
    public string? UserCV { get; set; }
    public string? InterviewJobTitle { get; set; }
    public string? InterviewCompany { get; set; }
    public List<string> PractisedQuestions { get; set; } = new();

    // Programming context
    public string? ProgrammingLanguage { get; set; }
    public string? CurrentCodeContext { get; set; }
}

public class JobContext
{
    public bool IsAnalyzed { get; set; }
    public string JobTitle { get; set; } = "Unknown Role";
    public string CompanyName { get; set; } = "Unknown Company";
    public string Location { get; set; } = "Not specified";
    public List<string> KeyRequirements { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public string RawJobText { get; set; } = "";
}

public class LanguageContext
{
    public string? NativeLanguageCode { get; set; }
    public string? TargetLanguageCode { get; set; }
    public string? Level { get; set; }
    public string? LearningGoal { get; set; }
}
