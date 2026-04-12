namespace SmartAssistApi.Client.Models;

public class ProfileContextToggles
{
    public bool IncludeBasicProfile { get; set; } = true;
    public bool IncludeSkills { get; set; } = true;
    public bool IncludeExperience { get; set; }
    public bool IncludeCv { get; set; }
    public string? ActiveTargetJobId { get; set; }
}

public record AgentRequest(
    string Message,
    string? SessionId = null,
    bool LanguageLearningMode = false,
    string? TargetLanguage = null,
    string? NativeLanguage = null,
    string? TargetLanguageCode = null,
    string? NativeLanguageCode = null,
    string? Level = null,
    string? LearningGoal = null,
    string? ToolType = null,
    ProfileContextToggles? ProfileToggles = null,
    string? CareerProfileUserId = null);

public class LanguageLearningResponse
{
    public string TargetLanguageText { get; set; } = "";
    public string NativeLanguageText { get; set; } = "";
    public string? LearnContext { get; set; }
    public string? LearnVariants { get; set; }
    public string? LearnTip { get; set; }
}

public record AgentResponse(
    string Reply,
    string? ToolUsed = null,
    LanguageLearningResponse? LearningData = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? Model = null,
    int? CacheCreationInputTokens = null,
    int? CacheReadInputTokens = null);

public class ChatMessage
{
    public string Text { get; set; } = "";
    public string? ToolUsed { get; set; }
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LanguageLearningResponse? LearningData { get; set; }
}
