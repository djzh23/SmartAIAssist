using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Chat session metadata and UI transcripts in Redis.
/// Keys: sessions:{userId}, conversation:{userId}:{sessionId}
/// </summary>
public sealed class ChatSessionService(
    IRedisStringStore redis,
    ConversationService conversationService,
    ILogger<ChatSessionService> logger)
{
    public const int MaxSessions = 50;
    private const int SessionsTtlSeconds = 90 * 24 * 3600;
    private const int MaxTranscriptJsonChars = 450_000;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private static string SessionsKey(string userId) => $"sessions:{userId}";
    private static string ConversationKey(string userId, string sessionId) => $"conversation:{userId}:{sessionId}";

    public static string ToConversationApiToolType(string storedToolType)
    {
        var t = storedToolType.Trim().ToLowerInvariant();
        return t switch
        {
            "interview" => "interviewprep",
            "interviewprep" => "interviewprep",
            _ => t,
        };
    }

    public static string DefaultTitleForTool(string toolType)
    {
        var t = toolType.Trim().ToLowerInvariant();
        return t switch
        {
            "job" or "stellenanalyse" or "job_analysis" or "jobanalyzer" => "Stellenanalyse",
            "interview" or "interviewprep" or "vorstellungsgespräch" => "Interview-Vorbereitung",
            "code" or "programmierung" or "programming" => "Code-Assistent",
            "language" or "sprachen" => "Sprachtraining",
            _ => "Karriere-Chat",
        };
    }

    public async Task<List<ChatSessionRecord>> GetSessions(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        var raw = await redis.GetAsync(SessionsKey(userId), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<ChatSessionRecord>>(raw, JsonOpts);
            return list ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialize sessions for user {UserId}", userId);
            return [];
        }
    }

    public async Task<ChatSessionRecord?> GetSession(string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessions(userId, cancellationToken).ConfigureAwait(false);
        return sessions.FirstOrDefault(s => s.Id == sessionId);
    }

    private async Task SaveSessions(string userId, List<ChatSessionRecord> sessions, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sessions, JsonOpts);
        await redis.SetAsync(SessionsKey(userId), json, SessionsTtlSeconds, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatSessionRecord> CreateSession(
        string userId,
        string toolType,
        string? title,
        CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessions(userId, cancellationToken).ConfigureAwait(false);
        var normalizedTool = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.Trim().ToLowerInvariant();
        var session = new ChatSessionRecord
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Title = string.IsNullOrWhiteSpace(title) ? DefaultTitleForTool(normalizedTool) : title.Trim(),
            ToolType = normalizedTool,
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow,
            MessageCount = 0,
        };

        sessions.Insert(0, session);

        if (sessions.Count > MaxSessions)
        {
            var toRemove = sessions.Skip(MaxSessions).ToList();
            sessions = sessions.Take(MaxSessions).ToList();
            foreach (var old in toRemove)
                await DeleteConversationAsync(userId, old.Id, old.ToolType, cancellationToken).ConfigureAwait(false);
        }

        await SaveSessions(userId, sessions, cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <summary>After a successful agent exchange: bump counts and set title from first user message when appropriate.</summary>
    public async Task NotifyAfterAgentMessageAsync(
        string userId,
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessions(userId, cancellationToken).ConfigureAwait(false);
        var session = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
            return;

        var wasFirst = session.MessageCount == 0;
        session.MessageCount++;
        session.LastMessageAt = DateTime.UtcNow;

        if (wasFirst)
        {
            var trimmed = userMessage.Trim();
            if (trimmed.Length > 0)
            {
                var title = trimmed.Length > 40 ? $"{trimmed[..40]}…" : trimmed;
                session.Title = title;
            }
        }

        await SaveSessions(userId, sessions, cancellationToken).ConfigureAwait(false);

        _ = TouchTranscriptTtlAsync(userId, sessionId, cancellationToken);
    }

    private async Task TouchTranscriptTtlAsync(string userId, string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var key = ConversationKey(userId, sessionId);
            var existing = await redis.GetAsync(key, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(existing))
                return;
            await redis.SetAsync(key, existing, SessionsTtlSeconds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "TTL refresh skipped for transcript {SessionId}", sessionId);
        }
    }

    public async Task DeleteSession(string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessions(userId, cancellationToken).ConfigureAwait(false);
        var removed = sessions.FirstOrDefault(s => s.Id == sessionId);
        sessions.RemoveAll(s => s.Id == sessionId);
        await SaveSessions(userId, sessions, cancellationToken).ConfigureAwait(false);

        if (removed is not null)
        {
            await DeleteConversationAsync(userId, sessionId, removed.ToolType, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            foreach (var t in AllConversationApiToolTypes)
                await conversationService.ClearSessionAsync(userId, sessionId, t).ConfigureAwait(false);
            await redis.DeleteAsync(ConversationKey(userId, sessionId), cancellationToken).ConfigureAwait(false);
        }
    }

    private static readonly string[] AllConversationApiToolTypes =
        ["general", "jobanalyzer", "language", "programming", "interviewprep"];

    public async Task RenameSession(string userId, string sessionId, string newTitle, CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessions(userId, cancellationToken).ConfigureAwait(false);
        var session = sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
            return;

        session.Title = newTitle.Trim();
        await SaveSessions(userId, sessions, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReorderSessions(string userId, IReadOnlyList<string> orderedSessionIds, CancellationToken cancellationToken = default)
    {
        var sessions = await GetSessions(userId, cancellationToken).ConfigureAwait(false);
        if (sessions.Count == 0)
            return;

        var idSet = sessions.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        if (orderedSessionIds.Count != idSet.Count || orderedSessionIds.Any(id => !idSet.Contains(id)))
            throw new ArgumentException("orderedSessionIds must contain every session id exactly once.");

        var byId = sessions.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var next = orderedSessionIds.Select(id => byId[id]).ToList();
        await SaveSessions(userId, next, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetTranscriptJson(string userId, string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await redis.GetAsync(ConversationKey(userId, sessionId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis get failed for transcript {UserId}/{SessionId}", userId, sessionId);
            return null;
        }
    }

    public async Task SaveTranscriptJson(string userId, string sessionId, string json, CancellationToken cancellationToken = default)
    {
        var trimmed = json.Length > MaxTranscriptJsonChars ? json[..MaxTranscriptJsonChars] : json;
        await redis.SetAsync(ConversationKey(userId, sessionId), trimmed, SessionsTtlSeconds, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteConversationAsync(string userId, string sessionId, string storedToolType, CancellationToken cancellationToken)
    {
        await redis.DeleteAsync(ConversationKey(userId, sessionId), cancellationToken).ConfigureAwait(false);
        var apiTool = ToConversationApiToolType(storedToolType);
        await conversationService.ClearSessionAsync(userId, sessionId, apiTool).ConfigureAwait(false);
    }
}
