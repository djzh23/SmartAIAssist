using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Chat notes in Redis (Upstash via <see cref="IRedisStringStore"/>).</summary>
public class ChatNotesRedisService(IRedisStringStore redis, ILogger<ChatNotesRedisService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string IndexKey(string userId) => $"chatnotes:v2:{userId}:index";

    private static string NoteKey(string userId, string noteId) => $"chatnotes:v2:{userId}:n:{noteId}";

    public async Task<IReadOnlyList<ChatNoteRecord>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        var ids = await LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        if (ids.Count == 0)
            return [];

        var loaded = await Task.WhenAll(ids.Select(id => GetByIdAsync(userId, id, cancellationToken))).ConfigureAwait(false);
        var list = new List<ChatNoteRecord>(ids.Count);
        for (var i = 0; i < loaded.Length; i++)
        {
            if (loaded[i] is not null)
                list.Add(loaded[i]!);
        }

        return list;
    }

    public async Task<ChatNoteRecord?> GetByIdAsync(string userId, string noteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(noteId))
            return null;

        try
        {
            var raw = await redis.StringGetAsync(NoteKey(userId, noteId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            return JsonSerializer.Deserialize<ChatNoteRecord>(raw, JsonOpts);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ChatNotes get failed for user {UserId} note {NoteId}", userId, noteId);
            return null;
        }
    }

    public async Task<ChatNoteRecord> CreateAsync(
        string userId,
        string title,
        string body,
        IReadOnlyList<string> tags,
        ChatNoteSource? source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var (t, b, tagList) = ChatNotesValidation.Normalize(title, body, tags);
        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var id = Guid.NewGuid().ToString("N");
        var record = new ChatNoteRecord
        {
            Id = id,
            CreatedAt = now,
            UpdatedAt = now,
            Title = t,
            Body = b,
            Tags = tagList,
            Source = source,
        };

        var payload = JsonSerializer.Serialize(record, JsonOpts);
        await redis.StringSetAsync(NoteKey(userId, id), payload, cancellationToken).ConfigureAwait(false);

        var index = await LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        index.Remove(id);
        index.Insert(0, id);
        index = await TrimIndexAndDeleteOverflowAsync(userId, index, cancellationToken).ConfigureAwait(false);
        await SaveIndexAsync(userId, index, cancellationToken).ConfigureAwait(false);

        return record;
    }

    public async Task<ChatNoteRecord?> UpdateAsync(
        string userId,
        string noteId,
        string? title,
        string? body,
        IReadOnlyList<string>? tags,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(noteId))
            return null;

        var existing = await GetByIdAsync(userId, noteId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return null;

        var nextTitle = title is not null ? ChatNotesValidation.NormalizeTitle(title) : existing.Title;
        var nextBody = body is not null ? ChatNotesValidation.NormalizeBody(body) : existing.Body;
        var nextTags = tags is not null ? ChatNotesValidation.NormalizeTags(tags) : existing.Tags;
        var now = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        var record = new ChatNoteRecord
        {
            Id = existing.Id,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
            Title = nextTitle,
            Body = nextBody,
            Tags = nextTags,
            Source = existing.Source,
        };

        var payload = JsonSerializer.Serialize(record, JsonOpts);
        await redis.StringSetAsync(NoteKey(userId, noteId), payload, cancellationToken).ConfigureAwait(false);

        var index = await LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        index.Remove(noteId);
        index.Insert(0, noteId);
        await SaveIndexAsync(userId, index, cancellationToken).ConfigureAwait(false);

        return record;
    }

    public async Task<bool> DeleteAsync(string userId, string noteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(noteId))
            return false;

        var existing = await GetByIdAsync(userId, noteId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        var index = await LoadIndexAsync(userId, cancellationToken).ConfigureAwait(false);
        _ = index.Remove(noteId);
        await SaveIndexAsync(userId, index, cancellationToken).ConfigureAwait(false);
        try
        {
            await redis.StringDeleteAsync(NoteKey(userId, noteId), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ChatNotes delete key failed for user {UserId} note {NoteId}", userId, noteId);
        }

        return true;
    }

    private async Task<List<string>> TrimIndexAndDeleteOverflowAsync(
        string userId,
        List<string> index,
        CancellationToken cancellationToken)
    {
        if (index.Count <= ChatNotesValidation.MaxNotesPerUser)
            return index;

        var overflow = index.Skip(ChatNotesValidation.MaxNotesPerUser).ToList();
        var kept = index.Take(ChatNotesValidation.MaxNotesPerUser).ToList();
        foreach (var oldId in overflow)
        {
            try
            {
                await redis.StringDeleteAsync(NoteKey(userId, oldId), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ChatNotes trim delete failed for user {UserId} note {NoteId}", userId, oldId);
            }
        }

        return kept;
    }

    private async Task<List<string>> LoadIndexAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await redis.StringGetAsync(IndexKey(userId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
                return [];
            var ids = JsonSerializer.Deserialize<List<string>>(raw, JsonOpts);
            return ids is null ? [] : ids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ChatNotes index read failed for user {UserId}", userId);
            return [];
        }
    }

    private async Task SaveIndexAsync(string userId, IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(ids.ToList(), JsonOpts);
        await redis.StringSetAsync(IndexKey(userId), payload, cancellationToken).ConfigureAwait(false);
    }
}
