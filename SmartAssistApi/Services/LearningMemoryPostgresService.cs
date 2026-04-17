using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Learning memory in PostgreSQL (Supabase).</summary>
public sealed class LearningMemoryPostgresService(SmartAssistDbContext db, ILogger<LearningMemoryPostgresService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = LearningMemoryRedisService.JsonOpts;

    public async Task<UserLearningMemory> GetMemory(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return new UserLearningMemory { UserId = userId };

        try
        {
            var row = await db.LearningMemories
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.ClerkUserId == userId, cancellationToken)
                .ConfigureAwait(false);
            if (row is null || string.IsNullOrWhiteSpace(row.MemoryJson))
                return new UserLearningMemory { UserId = userId };

            var mem = JsonSerializer.Deserialize<UserLearningMemory>(row.MemoryJson, JsonOpts);
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

    public async Task AddInsight(string userId, LearningInsight insight, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            var memory = await GetMemoryForUpdateAsync(userId, cancellationToken).ConfigureAwait(false);
            LearningMemoryInsightMutations.AddInsight(memory, insight, userId);
            await UpsertMemoryAsync(userId, memory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory write failed for user {UserId}", userId);
        }
    }

    public async Task ResolveInsight(string userId, string insightId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(insightId))
            return;

        try
        {
            var memory = await GetMemoryForUpdateAsync(userId, cancellationToken).ConfigureAwait(false);
            if (!LearningMemoryInsightMutations.TryResolveInsight(memory, insightId, userId))
                return;
            await UpsertMemoryAsync(userId, memory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory resolve failed for user {UserId} insight {InsightId}", userId, insightId);
        }
    }

    public async Task PatchInsight(
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
            var memory = await GetMemoryForUpdateAsync(userId, cancellationToken).ConfigureAwait(false);
            if (!LearningMemoryInsightMutations.TryPatchInsight(memory, insightId, title, content, resolved, userId))
                return;
            await UpsertMemoryAsync(userId, memory, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory patch failed for user {UserId} insight {InsightId}", userId, insightId);
        }
    }

    /// <summary>Imports JSON from Redis backfill (raw document).</summary>
    public async Task ImportFromRedisJsonAsync(string userId, string? json, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (string.IsNullOrWhiteSpace(json))
            return;

        UserLearningMemory mem;
        try
        {
            mem = JsonSerializer.Deserialize<UserLearningMemory>(json, JsonOpts)
                ?? new UserLearningMemory { UserId = userId };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory import JSON invalid for user {UserId}", userId);
            return;
        }

        mem.UserId = userId;
        mem.Insights ??= new List<LearningInsight>();
        await UpsertMemoryAsync(userId, mem, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserLearningMemory> GetMemoryForUpdateAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await db.LearningMemories
            .FirstOrDefaultAsync(e => e.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null || string.IsNullOrWhiteSpace(row.MemoryJson))
            return new UserLearningMemory { UserId = userId };

        var mem = JsonSerializer.Deserialize<UserLearningMemory>(row.MemoryJson, JsonOpts);
        if (mem is null)
            return new UserLearningMemory { UserId = userId };
        mem.UserId = userId;
        mem.Insights ??= new List<LearningInsight>();
        return mem;
    }

    private async Task UpsertMemoryAsync(string userId, UserLearningMemory memory, CancellationToken cancellationToken)
    {
        memory.UserId = userId;
        memory.UpdatedAt = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(memory, JsonOpts);
        var now = DateTime.UtcNow;
        var row = await db.LearningMemories
            .FirstOrDefaultAsync(e => e.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            db.LearningMemories.Add(new LearningMemoryEntity
            {
                ClerkUserId = userId,
                MemoryJson = payload,
                UpdatedAt = now,
            });
        }
        else
        {
            row.MemoryJson = payload;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
