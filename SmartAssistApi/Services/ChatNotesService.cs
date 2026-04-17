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
/// Falls back to Redis transparently if a Postgres call throws at runtime.
/// </summary>
public sealed class ChatNotesService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    ChatNotesRedisService redis,
    IServiceProvider serviceProvider,
    ILogger<ChatNotesService> logger)
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

    public async Task<IReadOnlyList<ChatNoteRecord>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (UsePostgres)
        {
            try { return await Postgres!.ListAsync(userId, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Postgres ListAsync failed; falling back to Redis"); }
        }
        return await redis.ListAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatNoteRecord?> GetByIdAsync(string userId, string noteId, CancellationToken cancellationToken = default)
    {
        if (UsePostgres)
        {
            try { return await Postgres!.GetByIdAsync(userId, noteId, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Postgres GetByIdAsync failed; falling back to Redis"); }
        }
        return await redis.GetByIdAsync(userId, noteId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatNoteRecord> CreateAsync(
        string userId,
        string title,
        string body,
        IReadOnlyList<string> tags,
        ChatNoteSource? source,
        CancellationToken cancellationToken = default)
    {
        if (UsePostgres)
        {
            try { return await Postgres!.CreateAsync(userId, title, body, tags, source, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Postgres CreateAsync failed; falling back to Redis"); }
        }
        return await redis.CreateAsync(userId, title, body, tags, source, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatNoteRecord?> UpdateAsync(
        string userId,
        string noteId,
        string? title,
        string? body,
        IReadOnlyList<string>? tags,
        CancellationToken cancellationToken = default)
    {
        if (UsePostgres)
        {
            try { return await Postgres!.UpdateAsync(userId, noteId, title, body, tags, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Postgres UpdateAsync failed; falling back to Redis"); }
        }
        return await redis.UpdateAsync(userId, noteId, title, body, tags, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string userId, string noteId, CancellationToken cancellationToken = default)
    {
        if (UsePostgres)
        {
            try { return await Postgres!.DeleteAsync(userId, noteId, cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Postgres DeleteAsync failed; falling back to Redis"); }
        }
        return await redis.DeleteAsync(userId, noteId, cancellationToken).ConfigureAwait(false);
    }
}
