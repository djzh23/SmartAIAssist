using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Where job applications are stored vs what was configured.</summary>
public readonly record struct ApplicationsBackendInfo(
    string EffectiveStorage,
    string ConfiguredJobApplicationsStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>Redis or PostgreSQL persistence for job applications API.</summary>
public interface IApplicationService
{
    ApplicationsBackendInfo GetBackendInfo();

    Task<List<JobApplicationDocument>> ListAsync(string userId, CancellationToken cancellationToken = default);

    Task<JobApplicationDocument?> GetAsync(string userId, string applicationId, CancellationToken cancellationToken = default);

    Task SaveAllAsync(string userId, List<JobApplicationDocument> apps, CancellationToken cancellationToken = default);

    /// <summary>Compact German block for system prompt when a job application id is active on the agent request.</summary>
    Task<string?> BuildPromptContextAsync(string userId, string? applicationId, CancellationToken cancellationToken = default);
}
