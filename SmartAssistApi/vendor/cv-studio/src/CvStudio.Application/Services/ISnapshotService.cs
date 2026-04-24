using CvStudio.Application.DTOs;

namespace CvStudio.Application.Services;

public interface ISnapshotService
{
    Task<ResumeVersionDto> CreateSnapshotAsync(string clerkUserId, Guid resumeId, CreateVersionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResumeVersionDto>> ListSnapshotsAsync(string clerkUserId, Guid resumeId, CancellationToken cancellationToken = default);
    Task<ResumeVersionDto> GetSnapshotAsync(string clerkUserId, Guid resumeId, Guid versionId, CancellationToken cancellationToken = default);
    Task<ResumeVersionDto> UpdateSnapshotAsync(string clerkUserId, Guid resumeId, Guid versionId, UpdateVersionRequest request, CancellationToken cancellationToken = default);
    Task DeleteSnapshotAsync(string clerkUserId, Guid resumeId, Guid versionId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the resume working copy (<see cref="Resume.CurrentContentJson"/>) with the snapshot payload; metadata (title, links, notes) unchanged.</summary>
    Task<ResumeDto> RestoreSnapshotToWorkingCopyAsync(string clerkUserId, Guid resumeId, Guid versionId, CancellationToken cancellationToken = default);
}

