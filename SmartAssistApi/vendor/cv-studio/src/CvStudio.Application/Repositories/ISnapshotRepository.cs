using CvStudio.Domain.Entities;

namespace CvStudio.Application.Repositories;

public interface ISnapshotRepository
{
    Task AddAsync(Snapshot version, CancellationToken cancellationToken = default);
    Task<int> GetNextVersionNumberAsync(Guid resumeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Snapshot>> ListByResumeIdAsync(Guid resumeId, CancellationToken cancellationToken = default);
    Task<Snapshot?> GetByIdAsync(Guid versionId, CancellationToken cancellationToken = default);
    Task<Snapshot?> GetByResumeAndVersionIdAsync(Guid resumeId, Guid versionId, CancellationToken cancellationToken = default);
    Task<Snapshot?> GetTrackedByResumeAndVersionIdAsync(Guid resumeId, Guid versionId, CancellationToken cancellationToken = default);
    void Remove(Snapshot version);
}

