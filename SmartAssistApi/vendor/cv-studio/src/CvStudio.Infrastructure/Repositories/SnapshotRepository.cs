using Microsoft.EntityFrameworkCore;
using CvStudio.Application.DTOs;
using CvStudio.Application.Repositories;
using CvStudio.Domain.Entities;
using CvStudio.Infrastructure.Persistence;

namespace CvStudio.Infrastructure.Repositories;

public sealed class SnapshotRepository : ISnapshotRepository
{
    private readonly CvStudioDbContext _dbContext;

    public SnapshotRepository(CvStudioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Snapshot version, CancellationToken cancellationToken = default)
    {
        await _dbContext.ResumeVersions.AddAsync(version, cancellationToken);
    }

    public async Task<int> GetNextVersionNumberAsync(Guid resumeId, CancellationToken cancellationToken = default)
    {
        var currentMax = await _dbContext.ResumeVersions
            .Where(x => x.ResumeId == resumeId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(cancellationToken);

        return (currentMax ?? 0) + 1;
    }

    public async Task<IReadOnlyList<Snapshot>> ListByResumeIdAsync(Guid resumeId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ResumeVersions
            .AsNoTracking()
            .Where(x => x.ResumeId == resumeId)
            .OrderByDescending(x => x.VersionNumber)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResumeVersionSummaryDto>> ListMetadataByResumeIdAsync(
        Guid resumeId,
        CancellationToken cancellationToken = default)
    {
        // Project to a lightweight DTO — EF Core translates this to a SELECT that omits
        // content_json entirely, so the large snapshot payloads are never sent over the wire.
        return await _dbContext.ResumeVersions
            .AsNoTracking()
            .Where(x => x.ResumeId == resumeId)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new ResumeVersionSummaryDto
            {
                Id = x.Id,
                ResumeId = x.ResumeId,
                VersionNumber = x.VersionNumber,
                Label = x.Label,
                CreatedAtUtc = x.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);
    }

    public Task<Snapshot?> GetByIdAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ResumeVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId, cancellationToken);
    }

    public Task<Snapshot?> GetByResumeAndVersionIdAsync(Guid resumeId, Guid versionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ResumeVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ResumeId == resumeId && x.Id == versionId, cancellationToken);
    }

    public Task<Snapshot?> GetTrackedByResumeAndVersionIdAsync(Guid resumeId, Guid versionId, CancellationToken cancellationToken = default)
    {
        return _dbContext.ResumeVersions
            .FirstOrDefaultAsync(x => x.ResumeId == resumeId && x.Id == versionId, cancellationToken);
    }

    public void Remove(Snapshot version)
    {
        _dbContext.ResumeVersions.Remove(version);
    }
}

