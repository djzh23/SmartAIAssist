using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Redis-backed job applications for <c>/api/applications</c> and agent context.</summary>
public sealed class ApplicationsRedisService(IRedisStringStore redis, ILogger<ApplicationsRedisService> logger)
{
    internal static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string AppsKey(string userId) => $"job_apps:{userId}";

    public async Task<List<JobApplicationDocument>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<JobApplicationDocument>();

        try
        {
            var json = await redis.StringGetAsync(AppsKey(userId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new List<JobApplicationDocument>();

            var list = JsonSerializer.Deserialize<List<JobApplicationDocument>>(json, JsonOpts);
            return list ?? new List<JobApplicationDocument>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list applications for {UserId}", userId);
            return new List<JobApplicationDocument>();
        }
    }

    public async Task<JobApplicationDocument?> GetAsync(string userId, string applicationId, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(userId, cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault(a => string.Equals(a.Id, applicationId, StringComparison.Ordinal));
    }

    public async Task SaveAllAsync(string userId, List<JobApplicationDocument> apps, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            foreach (var a in apps)
                a.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(apps, JsonOpts);
            await redis.StringSetAsync(AppsKey(userId), json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save applications for {UserId}", userId);
            throw;
        }
    }

    /// <summary>Compact German block for system prompt when job application id is set on the agent request.</summary>
    public async Task<string?> BuildPromptContextAsync(string userId, string? applicationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            return null;

        var app = await GetAsync(userId, applicationId, cancellationToken).ConfigureAwait(false);
        return JobApplicationPromptContext.Build(app);
    }
}
