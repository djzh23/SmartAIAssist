using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Job applications in Supabase/PostgreSQL via EF Core.</summary>
public sealed class ApplicationsPostgresService(SmartAssistDbContext db, ILogger<ApplicationsPostgresService> logger)
{
    public async Task<List<JobApplicationDocument>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        var rows = await db.JobApplications
            .AsNoTracking()
            .Where(a => a.ClerkUserId == userId)
            .OrderByDescending(a => a.UpdatedAt)
            .ThenByDescending(a => a.ApplicationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(ToDocument).ToList();
    }

    public async Task<JobApplicationDocument?> GetAsync(string userId, string applicationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(applicationId))
            return null;

        var row = await db.JobApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.ClerkUserId == userId && a.ApplicationId == applicationId,
                cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToDocument(row);
    }

    public async Task SaveAllAsync(string userId, List<JobApplicationDocument> apps, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return;

        // Preserve list order when many rows share the same clock tick (matches Insert(0) = newest first).
        var now = DateTime.UtcNow;
        for (var i = 0; i < apps.Count; i++)
            apps[i].UpdatedAt = now.AddTicks(-i);

        await ReplaceAllCoreAsync(userId, apps, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Upserts documents using their existing <see cref="JobApplicationDocument.UpdatedAt"/> (e.g. Redis backfill).</summary>
    public async Task ImportDocumentsPreservingTimestampsAsync(
        string userId,
        IReadOnlyList<JobApplicationDocument> apps,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        await ReplaceAllCoreAsync(userId, apps.ToList(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> BuildPromptContextAsync(string userId, string? applicationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            return null;

        var app = await GetAsync(userId, applicationId, cancellationToken).ConfigureAwait(false);
        return JobApplicationPromptContext.Build(app);
    }

    private async Task ReplaceAllCoreAsync(string userId, List<JobApplicationDocument> apps, CancellationToken cancellationToken)
    {
        await EnsureAppUserAsync(userId, cancellationToken).ConfigureAwait(false);

        var ids = apps.Select(a => a.Id).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var toRemove = await db.JobApplications
            .Where(a => a.ClerkUserId == userId && !ids.Contains(a.ApplicationId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (toRemove.Count > 0)
            db.JobApplications.RemoveRange(toRemove);

        foreach (var doc in apps)
        {
            var existing = await db.JobApplications
                .FirstOrDefaultAsync(
                    a => a.ClerkUserId == userId && a.ApplicationId == doc.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
                db.JobApplications.Add(ToEntity(doc, userId));
            else
                CopyToEntity(existing, doc);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAppUserAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            if (await db.AppUsers.AsNoTracking().AnyAsync(u => u.ClerkUserId == userId, cancellationToken).ConfigureAwait(false))
                return;

            var now = DateTime.UtcNow;
            db.AppUsers.Add(new AppUserEntity { ClerkUserId = userId, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            if (await db.AppUsers.AsNoTracking().AnyAsync(u => u.ClerkUserId == userId, cancellationToken).ConfigureAwait(false))
                return;
            logger.LogError(ex, "Failed to ensure app_users row for {UserId}", userId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure app_users row for {UserId}", userId);
            throw;
        }
    }

    private static JobApplicationDocument ToDocument(JobApplicationEntity e)
    {
        List<ApplicationTimelineEvent> timeline = [];
        if (!string.IsNullOrWhiteSpace(e.TimelineJson))
        {
            try
            {
                timeline = JsonSerializer.Deserialize<List<ApplicationTimelineEvent>>(e.TimelineJson, ApplicationsRedisService.JsonOpts)
                    ?? [];
            }
            catch
            {
                timeline = [];
            }
        }

        return new JobApplicationDocument
        {
            Id = e.ApplicationId,
            JobTitle = e.JobTitle,
            Company = e.Company,
            JobUrl = e.JobUrl,
            JobDescription = e.JobDescription,
            Status = e.Status,
            StatusUpdatedAt = e.StatusUpdatedAt,
            TailoredCvNotes = e.TailoredCvNotes,
            CoverLetterText = e.CoverLetterText,
            InterviewNotes = e.InterviewNotes,
            Timeline = timeline,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
            AnalysisSessionId = e.AnalysisSessionId,
            InterviewSessionId = e.InterviewSessionId,
        };
    }

    private static JobApplicationEntity ToEntity(JobApplicationDocument d, string clerkUserId)
    {
        var entity = new JobApplicationEntity { ClerkUserId = clerkUserId, ApplicationId = d.Id };
        CopyToEntity(entity, d);
        return entity;
    }

    private static void CopyToEntity(JobApplicationEntity e, JobApplicationDocument d)
    {
        e.JobTitle = d.JobTitle;
        e.Company = d.Company;
        e.JobUrl = d.JobUrl;
        e.JobDescription = d.JobDescription;
        e.Status = string.IsNullOrWhiteSpace(d.Status) ? "draft" : d.Status;
        e.StatusUpdatedAt = d.StatusUpdatedAt;
        e.TailoredCvNotes = d.TailoredCvNotes;
        e.CoverLetterText = d.CoverLetterText;
        e.InterviewNotes = d.InterviewNotes;
        e.TimelineJson = JsonSerializer.Serialize(d.Timeline ?? [], ApplicationsRedisService.JsonOpts);
        e.CreatedAt = d.CreatedAt;
        e.UpdatedAt = d.UpdatedAt;
        e.AnalysisSessionId = d.AnalysisSessionId;
        e.InterviewSessionId = d.InterviewSessionId;
    }
}
