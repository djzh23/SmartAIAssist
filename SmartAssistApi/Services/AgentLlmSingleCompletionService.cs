namespace SmartAssistApi.Services;

/// <summary>Delegates <see cref="ILlmSingleCompletionService"/> to <see cref="AgentService.SingleCompletion"/>.</summary>
public sealed class AgentLlmSingleCompletionService(AgentService agent) : ILlmSingleCompletionService
{
    public Task<string> CompleteAsync(string prompt, int maxTokens, CancellationToken cancellationToken = default) =>
        agent.SingleCompletion(prompt, maxTokens);
}
