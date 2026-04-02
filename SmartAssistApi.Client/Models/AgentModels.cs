namespace SmartAssistApi.Client.Models;

public record AgentRequest(
    string Message,
    string? SessionId = null,
    bool LanguageLearningMode = false,
    string? TargetLanguage = null,
    string? NativeLanguage = null,
    string? TargetLanguageCode = null,
    string? NativeLanguageCode = null,
    string? Level = null,
    string? LearningGoal = null
);

public record AgentResponse(string Reply, string? ToolUsed = null);

public class ChatMessage
{
    public string Text { get; init; } = "";
    public string? ToolUsed { get; init; }
    public bool IsUser { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
