using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public interface IAgentService
{
    Task<AgentResponse> RunAsync(AgentRequest request);
    IAsyncEnumerable<AgentStreamChunk> StreamAsync(AgentRequest request, CancellationToken ct = default);
}

/// <summary>One unit of a streaming agent response.</summary>
public readonly record struct AgentStreamChunk
{
    public string? Text    { get; init; }
    public bool   IsDone  { get; init; }
    public string? ToolUsed { get; init; }

    public static AgentStreamChunk TextPart(string text)            => new() { Text = text };
    public static AgentStreamChunk Done(string? toolUsed = null)    => new() { IsDone = true, ToolUsed = toolUsed };
}
