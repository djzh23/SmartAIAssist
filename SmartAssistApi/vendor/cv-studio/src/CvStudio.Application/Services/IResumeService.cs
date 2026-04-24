using CvStudio.Application.DTOs;

namespace CvStudio.Application.Services;

public interface IResumeService
{
    Task<IReadOnlyList<ResumeSummaryDto>> ListAsync(string clerkUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResumeTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default);
    Task<int> DeleteAllAsync(string clerkUserId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string clerkUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ResumeDto> CreateFromTemplateAsync(
        string clerkUserId,
        string templateKey,
        LinkJobApplicationRequest? linkAfterCreate,
        CancellationToken cancellationToken = default);
    Task<ResumeDto> CreateAsync(string clerkUserId, CreateResumeRequest request, CancellationToken cancellationToken = default);
    Task<ResumeDto> GetCurrentAsync(string clerkUserId, Guid id, CancellationToken cancellationToken = default);
    Task<ResumeDto> UpdateAsync(string clerkUserId, Guid id, UpdateResumeRequest request, CancellationToken cancellationToken = default);
    Task<ResumeDto> LinkJobApplicationAsync(string clerkUserId, Guid id, LinkJobApplicationRequest request, CancellationToken cancellationToken = default);
    Task<ResumeDto> PatchNotesAsync(string clerkUserId, Guid id, PatchResumeNotesRequest request, CancellationToken cancellationToken = default);
}

