using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;

namespace SmartAssistApi.Services;

public sealed class CvStudioPdfExportService(SmartAssistDbContext db, UsageService usage)
{
    public async Task<int> CountAsync(string clerkUserId, CancellationToken cancellationToken = default) =>
        await db.CvPdfExports.AsNoTracking().CountAsync(x => x.ClerkUserId == clerkUserId, cancellationToken);

    public async Task<(int limit, int used)> GetQuotaAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        var plan = await usage.GetPlanAsync(clerkUserId).ConfigureAwait(false);
        var limit = CvStudioPdfExportRules.LimitForPlan(plan);
        var used = await CountAsync(clerkUserId, cancellationToken).ConfigureAwait(false);
        return (limit, used);
    }

    public async Task<IReadOnlyList<CvPdfExportEntity>> ListAsync(string clerkUserId, CancellationToken cancellationToken = default) =>
        await db.CvPdfExports.AsNoTracking()
            .Where(x => x.ClerkUserId == clerkUserId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<CvPdfExportEntity> RecordPdfExportAsync(
        string clerkUserId,
        Guid resumeId,
        Guid? versionId,
        string design,
        string fileLabel,
        CancellationToken cancellationToken = default)
    {
        var (limit, used) = await GetQuotaAsync(clerkUserId, cancellationToken).ConfigureAwait(false);
        if (used >= limit)
            throw new CvStudioPdfQuotaExceededException(limit, used);

        var row = new CvPdfExportEntity
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            ResumeId = resumeId,
            VersionId = versionId,
            Design = NormalizeDesign(design),
            FileLabel = fileLabel,
            CreatedAt = DateTime.UtcNow,
            StorageObjectPath = null,
        };
        db.CvPdfExports.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return row;
    }

    public async Task<bool> TryDeleteAsync(string clerkUserId, Guid exportId, CancellationToken cancellationToken = default)
    {
        var deleted = await db.CvPdfExports
            .Where(x => x.Id == exportId && x.ClerkUserId == clerkUserId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return deleted > 0;
    }

    /// <summary>Removes tracked PDF rows for a resume (e.g. before deleting the resume).</summary>
    public Task<int> DeleteExportsForResumeAsync(string clerkUserId, Guid resumeId, CancellationToken cancellationToken = default) =>
        db.CvPdfExports
            .Where(x => x.ClerkUserId == clerkUserId && x.ResumeId == resumeId)
            .ExecuteDeleteAsync(cancellationToken);

    private static string NormalizeDesign(string? design)
    {
        if (string.IsNullOrWhiteSpace(design))
            return "A";
        var t = design.Trim().ToUpperInvariant();
        return t.Length > 8 ? t[..8] : t;
    }
}

public sealed class CvStudioPdfQuotaExceededException(int limit, int used) : Exception(
    $"PDF-Export-Limit erreicht ({used}/{limit}). Lösche einen gespeicherten PDF-Eintrag in CV.Studio oder upgrade deinen Tarif.")
{
    public int Limit { get; } = limit;
    public int Used { get; } = used;
}
