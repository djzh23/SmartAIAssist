using Microsoft.Extensions.Options;
using SmartAssistApi.Data;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Where chat notes are actually stored vs what was configured.</summary>
public readonly record struct ChatNotesBackendInfo(
    string EffectiveStorage,
    string ConfiguredChatNotesStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>
/// Routes chat-notes persistence to Redis or PostgreSQL based on <see cref="DatabaseFeatureOptions"/>.
/// </summary>
public sealed class ChatNotesService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    ChatNotesRedisService redis,
    IServiceProvider serviceProvider)
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private ChatNotesPostgresService? Postgres =>
        serviceProvider.GetService(typeof(ChatNotesPostgresService)) as ChatNotesPostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    /// <summary>Used for response headers and client-visible degraded-mode disclosure.</summary>
    public ChatNotesBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.ChatNotesStorage)
            ? "redis"
            : _opts.ChatNotesStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new ChatNotesBackendInfo(effective, configured, degraded, reason);
    }

    public Task<IReadOnlyList<ChatNoteRecord>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.ListAsync(userId, cancellationToken)
            : redis.ListAsync(userId, cancellationToken);

    public Task<ChatNoteRecord?> GetByIdAsync(string userId, string noteId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetByIdAsync(userId, noteId, cancellationToken)
            : redis.GetByIdAsync(userId, noteId, cancellationToken);

    public Task<ChatNoteRecord> CreateAsync(
        string userId,
        string title,
        string body,
        IReadOnlyList<string> tags,
        ChatNoteSource? source,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.CreateAsync(userId, title, body, tags, source, cancellationToken)
            : redis.CreateAsync(userId, title, body, tags, source, cancellationToken);

    public Task<ChatNoteRecord?> UpdateAsync(
        string userId,
        string noteId,
        string? title,
        string? body,
        IReadOnlyList<string>? tags,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.UpdateAsync(userId, noteId, title, body, tags, cancellationToken)
            : redis.UpdateAsync(userId, noteId, title, body, tags, cancellationToken);

    public Task<bool> DeleteAsync(string userId, string noteId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.DeleteAsync(userId, noteId, cancellationToken)
            : redis.DeleteAsync(userId, noteId, cancellationToken);
}
