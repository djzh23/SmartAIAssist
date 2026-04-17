using Microsoft.Extensions.Options;
using SmartAssistApi.Data;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Routes job applications to Redis or PostgreSQL based on <see cref="DatabaseFeatureOptions"/>.
/// </summary>
public sealed class ApplicationsService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    ApplicationsRedisService redis,
    IServiceProvider serviceProvider) : IApplicationService
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private ApplicationsPostgresService? Postgres =>
        serviceProvider.GetService(typeof(ApplicationsPostgresService)) as ApplicationsPostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.JobApplicationsStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    /// <inheritdoc />
    public ApplicationsBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.JobApplicationsStorage)
            ? "redis"
            : _opts.JobApplicationsStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new ApplicationsBackendInfo(effective, configured, degraded, reason);
    }

    /// <inheritdoc />
    public Task<List<JobApplicationDocument>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.ListAsync(userId, cancellationToken)
            : redis.ListAsync(userId, cancellationToken);

    /// <inheritdoc />
    public Task<JobApplicationDocument?> GetAsync(string userId, string applicationId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetAsync(userId, applicationId, cancellationToken)
            : redis.GetAsync(userId, applicationId, cancellationToken);

    /// <inheritdoc />
    public Task SaveAllAsync(string userId, List<JobApplicationDocument> apps, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.SaveAllAsync(userId, apps, cancellationToken)
            : redis.SaveAllAsync(userId, apps, cancellationToken);

    /// <inheritdoc />
    public Task<string?> BuildPromptContextAsync(string userId, string? applicationId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.BuildPromptContextAsync(userId, applicationId, cancellationToken)
            : redis.BuildPromptContextAsync(userId, applicationId, cancellationToken);
}
