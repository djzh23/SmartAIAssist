namespace SmartAssistApi.Models;

/// <summary>OpenAI-compatible chat message for Groq API.</summary>
public sealed class GroqChatMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = "";
}

/// <summary>Result of a Groq (or other OpenAI-compatible) completion call.</summary>
public sealed class GroqCompletionResult
{
    public bool Success { get; init; }
    public string Content { get; init; } = "";
    public string Model { get; init; } = "";
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public string? Error { get; init; }
}
