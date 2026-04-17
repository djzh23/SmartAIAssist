using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Learning memory in Redis (Upstash via <see cref="IRedisStringStore"/>). Key: learning:{userId}</summary>
public class LearningMemoryRedisService(IRedisStringStore redis, ILogger<LearningMemoryRedisService> logger)
{
    internal static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string MemoryKey(string userId) => $"learning:{userId}";

    public virtual async Task<UserLearningMemory> GetMemory(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return new UserLearningMemory { UserId = userId };

        try
        {
            var json = await redis.StringGetAsync(MemoryKey(userId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new UserLearningMemory { UserId = userId };

            var mem = JsonSerializer.Deserialize<UserLearningMemory>(json, JsonOpts);
            if (mem is null)
                return new UserLearningMemory { UserId = userId };
            mem.UserId = userId;
            mem.Insights ??= new List<LearningInsight>();
            return mem;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory read failed for user {UserId}", userId);
            return new UserLearningMemory { UserId = userId };
        }
    }

    public virtual async Task AddInsight(string userId, LearningInsight insight, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            var memory = await GetMemory(userId, cancellationToken).ConfigureAwait(false);
            LearningMemoryInsightMutations.AddInsight(memory, insight, userId);
            await SaveMemoryAsync(userId, memory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory write failed for user {UserId}", userId);
        }
    }

    public virtual async Task ResolveInsight(string userId, string insightId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(insightId))
            return;

        try
        {
            var memory = await GetMemory(userId, cancellationToken).ConfigureAwait(false);
            if (!LearningMemoryInsightMutations.TryResolveInsight(memory, insightId, userId))
                return;
            await SaveMemoryAsync(userId, memory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory resolve failed for user {UserId} insight {InsightId}", userId, insightId);
        }
    }

    public virtual async Task PatchInsight(
        string userId,
        string insightId,
        string? title,
        string? content,
        bool? resolved,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(insightId))
            return;

        try
        {
            var memory = await GetMemory(userId, cancellationToken).ConfigureAwait(false);
            if (!LearningMemoryInsightMutations.TryPatchInsight(memory, insightId, title, content, resolved, userId))
                return;
            await SaveMemoryAsync(userId, memory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory patch failed for user {UserId} insight {InsightId}", userId, insightId);
        }
    }

    private async Task SaveMemoryAsync(string userId, UserLearningMemory memory, CancellationToken cancellationToken)
    {
        memory.UserId = userId;
        var payload = JsonSerializer.Serialize(memory, JsonOpts);
        await redis.StringSetAsync(MemoryKey(userId), payload, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Copies Redis payload into Postgres import (same JSON shape).</summary>
    public Task<string?> GetRawJsonAsync(string userId, CancellationToken cancellationToken = default) =>
        redis.StringGetAsync(MemoryKey(userId), cancellationToken);
}
