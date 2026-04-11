namespace SmartAssistApi.Models;

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
