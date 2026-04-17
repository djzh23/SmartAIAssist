using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Persists chat session index + transcripts in PostgreSQL (Supabase).</summary>
public sealed class ChatSessionPostgresService(SmartAssistDbContext db, ILogger<ChatSessionPostgresService> logger)
{
    public async Task NotifyAfterAgentMessageAsync(
        string userId,
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(sessionId))
            return;

        try
        {
            var entity = await db.ChatSessions
                .FirstOrDefaultAsync(
                    s => s.ClerkUserId == userId && s.SessionId == sessionId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (entity is null)
                return;

            entity.LastMessageAt = DateTime.UtcNow;
            entity.MessageCount = Math.Max(0, entity.MessageCount) + 1;
            if (string.IsNullOrWhiteSpace(entity.Title) && !string.IsNullOrWhiteSpace(userMessage))
            {
                var t = userMessage.Trim().Replace('\n', ' ');
                entity.Title = t.Length > 80 ? t[..80] + "…" : t;
            }

            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chat session notify failed for user {UserId} session {SessionId}", userId, sessionId);
        }
    }

    public async Task<List<ChatSessionRecord>> LoadIndexAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<ChatSessionRecord>();

        try
        {
            var rows = await db.ChatSessions
                .AsNoTracking()
                .Where(s => s.ClerkUserId == userId)
                .OrderBy(s => s.DisplayOrder)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return rows.Select(ToRecord).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load chat session index for {UserId}", userId);
            return new List<ChatSessionRecord>();
        }
    }

    public async Task SaveIndexAsync(string userId, List<ChatSessionRecord> rows, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wantIds = rows.Select(r => r.Id).ToList();
            if (wantIds.Count == 0)
            {
                await db.ChatSessions
                    .Where(s => s.ClerkUserId == userId)
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await db.ChatSessions
                    .Where(s => s.ClerkUserId == userId && !wantIds.Contains(s.SessionId))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            var now = DateTime.UtcNow;
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var entity = await db.ChatSessions
                    .FirstOrDefaultAsync(
                        s => s.ClerkUserId == userId && s.SessionId == r.Id,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (entity is null)
                {
                    db.ChatSessions.Add(new ChatSessionEntity
                    {
                        ClerkUserId = userId,
                        SessionId = r.Id,
                        Title = r.Title,
                        ToolType = r.ToolType,
                        CreatedAt = EnsureUtc(r.CreatedAt),
                        LastMessageAt = EnsureUtc(r.LastMessageAt),
                        MessageCount = r.MessageCount,
                        DisplayOrder = i,
                        UpdatedAt = now,
                    });
                }
                else
                {
                    entity.Title = r.Title;
                    entity.ToolType = r.ToolType;
                    entity.CreatedAt = EnsureUtc(r.CreatedAt);
                    entity.LastMessageAt = EnsureUtc(r.LastMessageAt);
                    entity.MessageCount = r.MessageCount;
                    entity.DisplayOrder = i;
                    entity.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SaveIndex failed for user {UserId}", userId);
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<(string ToolType, string MessagesJson)?> GetTranscriptAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var row = await db.ChatTranscripts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => t.ClerkUserId == userId && t.SessionId == sessionId,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return null;

        return (row.ToolType, NormalizeMessagesJson(row.MessagesJson));
    }

    public async Task SaveTranscriptAsync(
        string userId,
        string sessionId,
        string toolType,
        string messagesJson,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (messagesJson ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            trimmed = "[]";

        var session = await db.ChatSessions
            .FirstOrDefaultAsync(s => s.ClerkUserId == userId && s.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            logger.LogWarning(
                "SaveTranscript: session {SessionId} missing for user {UserId}; skipping (FK)",
                sessionId,
                userId);
            return;
        }

        var now = DateTime.UtcNow;
        var tr = await db.ChatTranscripts
            .FirstOrDefaultAsync(t => t.ClerkUserId == userId && t.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (tr is null)
        {
            db.ChatTranscripts.Add(new ChatTranscriptEntity
            {
                ClerkUserId = userId,
                SessionId = sessionId,
                ToolType = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.Trim(),
                MessagesJson = trimmed,
                UpdatedAt = now,
            });
        }
        else
        {
            tr.ToolType = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.Trim();
            tr.MessagesJson = trimmed;
            tr.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteTranscriptAsync(string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        await db.ChatTranscripts
            .Where(t => t.ClerkUserId == userId && t.SessionId == sessionId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Copies Redis session index + transcripts into Postgres for one user.</summary>
    public async Task ImportFromRedisAsync(string userId, ChatSessionRedisService redis, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var list = await redis.LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        await SaveIndexAsync(userId, list, cancellationToken).ConfigureAwait(false);
        foreach (var s in list)
        {
            var t = await redis.GetTranscriptAsync(userId, s.Id, cancellationToken).ConfigureAwait(false);
            if (t is null)
                continue;
            await SaveTranscriptAsync(userId, s.Id, t.Value.ToolType, t.Value.MessagesJson, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static ChatSessionRecord ToRecord(ChatSessionEntity e) =>
        new()
        {
            Id = e.SessionId,
            Title = e.Title,
            ToolType = e.ToolType,
            CreatedAt = e.CreatedAt,
            LastMessageAt = e.LastMessageAt,
            MessageCount = e.MessageCount,
        };

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    /// <summary>Ensures API returns a JSON array string (same contract as Redis transcript extraction).</summary>
    private static string NormalizeMessagesJson(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return "[]";
        try
        {
            using var doc = JsonDocument.Parse(stored);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return "[]";
            return doc.RootElement.GetRawText();
        }
        catch
        {
            return "[]";
        }
    }
}
