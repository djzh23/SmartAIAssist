using CvStudio.Application.DTOs;
using CvStudio.Domain.Entities;

namespace CvStudio.Application.Repositories;

public interface IResumeRepository
{
    Task<IReadOnlyList<Resume>> ListAsync(string clerkUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight list for the overview page. Extracts profile preview fields from the JSONB
    /// column at the database level so the full content document is never sent over the wire.
    /// </summary>
    Task<IReadOnlyList<ResumeSummaryProjection>> ListSummariesAsync(string clerkUserId, CancellationToken cancellationToken = default);
    Task<Resume?> GetByIdAsync(Guid id, string clerkUserId, CancellationToken cancellationToken = default);
    Task AddAsync(Resume resume, CancellationToken cancellationToken = default);
    Task UpdateAsync(Resume resume, CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(string clerkUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes one resume owned by the user (snapshots cascade in DB).</summary>
    Task<int> DeleteByIdAsync(Guid id, string clerkUserId, CancellationToken cancellationToken = default);
}

