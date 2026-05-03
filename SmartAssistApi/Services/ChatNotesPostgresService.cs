using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Chat notes in Supabase/PostgreSQL via EF Core.</summary>
public sealed class ChatNotesPostgresService(SmartAssistDbContext db, ILogger<ChatNotesPostgresService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<ChatNoteRecord>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        var rows = await db.ChatNotes
            .AsNoTracking()
            .Where(n => n.ClerkUserId == userId)
            .OrderByDescending(n => n.UpdatedAt)
            .Take(ChatNotesValidation.MaxNotesPerUser)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToRecord).ToList();
    }

    public async Task<ChatNoteRecord?> GetByIdAsync(string userId, string noteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(noteId))
            return null;

        var row = await db.ChatNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.ClerkUserId == userId && n.Id == noteId, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToRecord(row);
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

        var now = DateTime.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var sourceJson = source is null ? null : JsonSerializer.Serialize(source, JsonOpts);

        var entity = new ChatNoteEntity
        {
            Id = id,
            ClerkUserId = userId,
            Title = t,
            Body = b,
            Tags = tagList.ToArray(),
            SourceJson = sourceJson,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.ChatNotes.Add(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await TrimOverflowAsync(userId, cancellationToken).ConfigureAwait(false);

        return ToRecord(entity);
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

        var entity = await db.ChatNotes
            .FirstOrDefaultAsync(n => n.ClerkUserId == userId && n.Id == noteId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return null;

        if (title is not null)
            entity.Title = ChatNotesValidation.NormalizeTitle(title);
        if (body is not null)
            entity.Body = ChatNotesValidation.NormalizeBody(body);
        if (tags is not null)
            entity.Tags = ChatNotesValidation.NormalizeTags(tags).ToArray();

        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToRecord(entity);
    }

    public async Task<bool> DeleteAsync(string userId, string noteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(noteId))
            return false;

        var entity = await db.ChatNotes
            .FirstOrDefaultAsync(n => n.ClerkUserId == userId && n.Id == noteId, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
            return false;

        db.ChatNotes.Remove(entity);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }


    private async Task TrimOverflowAsync(string userId, CancellationToken cancellationToken)
    {
        var count = await db.ChatNotes.CountAsync(n => n.ClerkUserId == userId, cancellationToken).ConfigureAwait(false);
        if (count <= ChatNotesValidation.MaxNotesPerUser)
            return;

        var overflow = count - ChatNotesValidation.MaxNotesPerUser;
        var victims = await db.ChatNotes
            .Where(n => n.ClerkUserId == userId)
            .OrderBy(n => n.UpdatedAt)
            .Take(overflow)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        db.ChatNotes.RemoveRange(victims);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static ChatNoteRecord ToRecord(ChatNoteEntity e)
    {
        ChatNoteSource? source = null;
        if (!string.IsNullOrWhiteSpace(e.SourceJson))
        {
            try
            {
                source = JsonSerializer.Deserialize<ChatNoteSource>(e.SourceJson, JsonOpts);
            }
            catch
            {
                /* ignore corrupt json */
            }
        }

        return new ChatNoteRecord
        {
            Id = e.Id,
            CreatedAt = e.CreatedAt.ToString("o", CultureInfo.InvariantCulture),
            UpdatedAt = e.UpdatedAt.ToString("o", CultureInfo.InvariantCulture),
            Title = e.Title,
            Body = e.Body,
            Tags = e.Tags.ToList(),
            Source = source,
        };
    }
}
