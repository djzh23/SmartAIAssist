using Microsoft.Extensions.Options;
using SmartAssistApi.Data;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Where chat sessions are actually stored vs what was configured.</summary>
public readonly record struct ChatSessionBackendInfo(
    string EffectiveStorage,
    string ConfiguredChatSessionStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>
/// Routes chat session index + transcripts to Redis or PostgreSQL based on <see cref="DatabaseFeatureOptions"/>.
/// </summary>
public sealed class ChatSessionService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    ChatSessionRedisService redis,
    IServiceProvider serviceProvider)
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private ChatSessionPostgresService? Postgres =>
        serviceProvider.GetService(typeof(ChatSessionPostgresService)) as ChatSessionPostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.ChatSessionStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    /// <summary>Used for response headers and client-visible degraded-mode disclosure.</summary>
    public ChatSessionBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.ChatSessionStorage)
            ? "redis"
            : _opts.ChatSessionStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new ChatSessionBackendInfo(effective, configured, degraded, reason);
    }

    public Task NotifyAfterAgentMessageAsync(
        string userId,
        string sessionId,
        string userMessage,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.NotifyAfterAgentMessageAsync(userId, sessionId, userMessage, cancellationToken)
            : redis.NotifyAfterAgentMessageAsync(userId, sessionId, userMessage, cancellationToken);

    public Task<List<ChatSessionRecord>> LoadIndexAsync(string userId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.LoadIndexAsync(userId, cancellationToken)
            : redis.LoadIndexAsync(userId, cancellationToken);

    public Task SaveIndexAsync(string userId, List<ChatSessionRecord> rows, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.SaveIndexAsync(userId, rows, cancellationToken)
            : redis.SaveIndexAsync(userId, rows, cancellationToken);

    public Task<(string ToolType, string MessagesJson)?> GetTranscriptAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetTranscriptAsync(userId, sessionId, cancellationToken)
            : redis.GetTranscriptAsync(userId, sessionId, cancellationToken);

    public Task SaveTranscriptAsync(
        string userId,
        string sessionId,
        string toolType,
        string messagesJson,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.SaveTranscriptAsync(userId, sessionId, toolType, messagesJson, cancellationToken)
            : redis.SaveTranscriptAsync(userId, sessionId, toolType, messagesJson, cancellationToken);

    public Task DeleteTranscriptAsync(string userId, string sessionId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.DeleteTranscriptAsync(userId, sessionId, cancellationToken)
            : redis.DeleteTranscriptAsync(userId, sessionId, cancellationToken);
}
