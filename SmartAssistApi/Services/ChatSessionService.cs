using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Maintains chat session index in Redis; bumps activity after each agent reply.</summary>
public class ChatSessionService(IRedisStringStore redis, ILogger<ChatSessionService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string IndexKey(string userId) => $"chat_sessions_index:{userId}";

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
            var json = await redis.StringGetAsync(IndexKey(userId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var list = JsonSerializer.Deserialize<List<ChatSessionRecord>>(json, JsonOpts);
            if (list is null || list.Count == 0)
                return;

            var row = list.FirstOrDefault(s => string.Equals(s.Id, sessionId, StringComparison.Ordinal));
            if (row is null)
                return;

            row.LastMessageAt = DateTime.UtcNow;
            row.MessageCount = Math.Max(0, row.MessageCount) + 1;
            if (string.IsNullOrWhiteSpace(row.Title) && !string.IsNullOrWhiteSpace(userMessage))
            {
                var t = userMessage.Trim().Replace('\n', ' ');
                row.Title = t.Length > 80 ? t[..80] + "…" : t;
            }

            var outJson = JsonSerializer.Serialize(list, JsonOpts);
            await redis.StringSetAsync(IndexKey(userId), outJson, cancellationToken).ConfigureAwait(false);
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
            var json = await redis.StringGetAsync(IndexKey(userId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new List<ChatSessionRecord>();

            return JsonSerializer.Deserialize<List<ChatSessionRecord>>(json, JsonOpts) ?? new List<ChatSessionRecord>();
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

        var json = JsonSerializer.Serialize(rows, JsonOpts);
        await redis.StringSetAsync(IndexKey(userId), json, cancellationToken).ConfigureAwait(false);
    }

    private static string TranscriptKey(string userId, string sessionId) => $"chat_transcript:{userId}:{sessionId}";

    public async Task<(string ToolType, string MessagesJson)?> GetTranscriptAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var raw = await redis.StringGetAsync(TranscriptKey(userId, sessionId), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var toolType = root.TryGetProperty("toolType", out var tt) ? tt.GetString() ?? "general" : "general";
            if (!root.TryGetProperty("messages", out var messages))
                return (toolType, "[]");
            return (toolType, messages.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    public Task SaveTranscriptAsync(
        string userId,
        string sessionId,
        string toolType,
        string messagesJson,
        CancellationToken cancellationToken = default)
    {
        var trimmed = (messagesJson ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            trimmed = "[]";
        var payload = $"{{\"toolType\":{JsonSerializer.Serialize(toolType)},\"messages\":{trimmed}}}";
        return redis.StringSetAsync(TranscriptKey(userId, sessionId), payload, cancellationToken);
    }

    public Task DeleteTranscriptAsync(string userId, string sessionId, CancellationToken cancellationToken = default) =>
        redis.StringDeleteAsync(TranscriptKey(userId, sessionId), cancellationToken);
}
