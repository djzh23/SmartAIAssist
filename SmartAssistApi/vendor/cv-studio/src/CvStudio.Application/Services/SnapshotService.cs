using Microsoft.Extensions.Logging;
using CvStudio.Application.DTOs;
using CvStudio.Application.Exceptions;
using CvStudio.Application.Repositories;
using CvStudio.Application.Validation;
using CvStudio.Domain.Entities;

namespace CvStudio.Application.Services;

public sealed class SnapshotService : ISnapshotService
{
    private readonly IResumeRepository _resumeRepository;
    private readonly ISnapshotRepository _versionRepository;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<SnapshotService> _logger;

    public SnapshotService(
        IResumeRepository resumeRepository,
        ISnapshotRepository versionRepository,
        IApplicationDbContext dbContext,
        ILogger<SnapshotService> logger)
    {
        _resumeRepository = resumeRepository;
        _versionRepository = versionRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ResumeVersionDto> CreateSnapshotAsync(string clerkUserId, Guid resumeId, CreateVersionRequest request, CancellationToken cancellationToken = default)
    {
        var errors = DataAnnotationsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UnprocessableEntityException(errors);
        }

        var resume = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

        var nextVersion = await _versionRepository.GetNextVersionNumberAsync(resumeId, cancellationToken);
        var version = new Snapshot
        {
            Id = Guid.NewGuid(),
            ResumeId = resumeId,
            VersionNumber = nextVersion,
            Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim(),
            ContentJson = resume.CurrentContentJson,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _versionRepository.AddAsync(version, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created version {VersionNumber} for resume {ResumeId}", version.VersionNumber, resumeId);

        return CvStudioMapper.ToDto(version);
    }

    public async Task<IReadOnlyList<ResumeVersionDto>> ListSnapshotsAsync(string clerkUserId, Guid resumeId, CancellationToken cancellationToken = default)
    {
        var resume = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

        _ = resume;

        var versions = await _versionRepository.ListByResumeIdAsync(resumeId, cancellationToken);
        return versions.Select(CvStudioMapper.ToDto).ToList();
    }

    public async Task<ResumeVersionDto> GetSnapshotAsync(string clerkUserId, Guid resumeId, Guid versionId, CancellationToken cancellationToken = default)
    {
        _ = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

        var version = await _versionRepository.GetByResumeAndVersionIdAsync(resumeId, versionId, cancellationToken)
            ?? throw new NotFoundException($"Version '{versionId}' for resume '{resumeId}' was not found.");

        return CvStudioMapper.ToDto(version);
    }

    public async Task<ResumeVersionDto> UpdateSnapshotAsync(string clerkUserId, Guid resumeId, Guid versionId, UpdateVersionRequest request, CancellationToken cancellationToken = default)
    {
        var errors = DataAnnotationsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new UnprocessableEntityException(errors);
        }

        _ = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

        var version = await _versionRepository.GetTrackedByResumeAndVersionIdAsync(resumeId, versionId, cancellationToken)
            ?? throw new NotFoundException($"Version '{versionId}' for resume '{resumeId}' was not found.");

        version.Label = string.IsNullOrWhiteSpace(request.Label) ? null : request.Label.Trim();

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Updated version {VersionId} for resume {ResumeId}", versionId, resumeId);

        return CvStudioMapper.ToDto(version);
    }

    public async Task DeleteSnapshotAsync(string clerkUserId, Guid resumeId, Guid versionId, CancellationToken cancellationToken = default)
    {
        _ = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

        var version = await _versionRepository.GetTrackedByResumeAndVersionIdAsync(resumeId, versionId, cancellationToken)
            ?? throw new NotFoundException($"Version '{versionId}' for resume '{resumeId}' was not found.");

        _versionRepository.Remove(version);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted version {VersionId} for resume {ResumeId}", versionId, resumeId);
    }
}

