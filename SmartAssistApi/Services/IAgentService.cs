using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public interface IAgentService
{
    Task<AgentResponse> RunAsync(AgentRequest request);
}
