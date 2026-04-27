using Microsoft.Extensions.Logging;
using CvStudio.Application.DTOs;
using CvStudio.Application.Exceptions;
using CvStudio.Application.Repositories;
using CvStudio.Application.Templates;
using CvStudio.Application.Validation;
using CvStudio.Domain.Entities;

namespace CvStudio.Application.Services;

public sealed class ResumeService : IResumeService
{
    private readonly IResumeRepository _resumeRepository;
    private readonly IApplicationDbContext _dbContext;
    private readonly ILogger<ResumeService> _logger;

    public ResumeService(
        IResumeRepository resumeRepository,
        IApplicationDbContext dbContext,
        ILogger<ResumeService> logger)
    {
        _resumeRepository = resumeRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<IReadOnlyList<ResumeTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        return Task.FromResult<IReadOnlyList<ResumeTemplateDto>>(ResumeTemplateCatalog.List());
    }

    public async Task<int> DeleteAllAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        var deleted = await _resumeRepository.DeleteAllAsync(clerkUserId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Deleted all resumes and snapshots for fresh start. Rows: {DeletedCount}", deleted);
        return deleted;
    }

    public async Task<IReadOnlyList<ResumeSummaryDto>> ListAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        // Use the optimised projection: JSONB fields are extracted at the DB level,
        // so the full content document (potentially hundreds of KB per resume) is
        // never loaded into memory for a list request.
        var projections = await _resumeRepository.ListSummariesAsync(clerkUserId, cancellationToken);
        return projections.Select(CvStudioMapper.ToSummaryDto).ToList();
    }

    public async Task<ResumeDto> CreateFromTemplateAsync(
        string clerkUserId,
        string templateKey,
        LinkJobApplicationRequest? linkAfterCreate,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeTemplateKeyRequired(templateKey);
        var template = ResumeTemplateCatalog.GetDefaultResume(normalizedKey);

        var now = DateTime.UtcNow;
        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            Title = template.Title,
            TemplateKey = normalizedKey,
            CurrentContentJson = CvStudioMapper.Serialize(template.Data),
            UpdatedAtUtc = now
        };

        await _resumeRepository.AddAsync(resume, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created resume {ResumeId} from template {TemplateKey}", resume.Id, normalizedKey);

        if (linkAfterCreate is not null
            && (!string.IsNullOrWhiteSpace(linkAfterCreate.JobApplicationId)
                || !string.IsNullOrWhiteSpace(linkAfterCreate.TargetCompany)
                || !string.IsNullOrWhiteSpace(linkAfterCreate.TargetRole)))
        {
            return await LinkJobApplicationAsync(clerkUserId, resume.Id, linkAfterCreate, cancellationToken)
                .ConfigureAwait(false);
        }

        return CvStudioMapper.ToDto(resume);
    }

    public async Task DeleteAsync(string clerkUserId, Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await _resumeRepository.DeleteByIdAsync(id, clerkUserId, cancellationToken).ConfigureAwait(false);
        if (deleted == 0)
            throw new NotFoundException($"Resume '{id}' was not found.");
    }

    public async Task<ResumeDto> CreateAsync(string clerkUserId, CreateResumeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOrThrow(request, request.ResumeData);

        var now = DateTime.UtcNow;
        var resume = new Resume
        {
            Id = Guid.NewGuid(),
            ClerkUserId = clerkUserId,
            Title = request.Title.Trim(),
            TemplateKey = NormalizeTemplateKey(request.TemplateKey),
            CurrentContentJson = CvStudioMapper.Serialize(request.ResumeData),
            UpdatedAtUtc = now
        };

        await _resumeRepository.AddAsync(resume, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created resume {ResumeId}", resume.Id);

        return CvStudioMapper.ToDto(resume);
    }

    public async Task<ResumeDto> GetCurrentAsync(string clerkUserId, Guid id, CancellationToken cancellationToken = default)
    {
        var resume = await _resumeRepository.GetByIdAsync(id, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{id}' was not found.");

        return CvStudioMapper.ToDto(resume);
    }

    public async Task<ResumeDto> UpdateAsync(string clerkUserId, Guid id, UpdateResumeRequest request, CancellationToken cancellationToken = default)
    {
        ValidateOrThrow(request, request.ResumeData);

        var resume = await _resumeRepository.GetByIdAsync(id, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{id}' was not found.");

        resume.Title = request.Title.Trim();
        resume.TemplateKey = NormalizeTemplateKey(request.TemplateKey) ?? resume.TemplateKey;
        resume.CurrentContentJson = CvStudioMapper.Serialize(request.ResumeData);
        resume.UpdatedAtUtc = DateTime.UtcNow;

        await _resumeRepository.UpdateAsync(resume, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated resume {ResumeId}", resume.Id);

        return CvStudioMapper.ToDto(resume);
    }

    public async Task<ResumeDto> LinkJobApplicationAsync(string clerkUserId, Guid id, LinkJobApplicationRequest request, CancellationToken cancellationToken = default)
    {
        var resume = await _resumeRepository.GetByIdAsync(id, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{id}' was not found.");

        resume.LinkedJobApplicationId = string.IsNullOrWhiteSpace(request.JobApplicationId) ? null : request.JobApplicationId.Trim();
        resume.TargetCompany = string.IsNullOrWhiteSpace(request.TargetCompany) ? null : request.TargetCompany.Trim();
        resume.TargetRole = string.IsNullOrWhiteSpace(request.TargetRole) ? null : request.TargetRole.Trim();

        // Auto-update title when linking to a job application if it still has the template default
        if (resume.LinkedJobApplicationId is not null
            && !string.IsNullOrWhiteSpace(resume.TargetCompany)
            && !string.IsNullOrWhiteSpace(resume.TargetRole))
        {
            var autoTitle = $"{resume.TargetCompany} — {resume.TargetRole}";
            if (autoTitle.Length <= 160)
                resume.Title = autoTitle;
        }

        resume.UpdatedAtUtc = DateTime.UtcNow;
        await _resumeRepository.UpdateAsync(resume, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Linked job application {AppId} to resume {ResumeId}", resume.LinkedJobApplicationId, id);
        return CvStudioMapper.ToDto(resume);
    }

    public async Task<ResumeDto> PatchNotesAsync(string clerkUserId, Guid id, PatchResumeNotesRequest request, CancellationToken cancellationToken = default)
    {
        var resume = await _resumeRepository.GetByIdAsync(id, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{id}' was not found.");

        resume.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        resume.UpdatedAtUtc = DateTime.UtcNow;
        await _resumeRepository.UpdateAsync(resume, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CvStudioMapper.ToDto(resume);
    }

    private static string? NormalizeTemplateKey(string? templateKey)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
        {
            return null;
        }

        return templateKey.Trim().ToLowerInvariant();
    }

    private static string NormalizeTemplateKeyRequired(string templateKey)
    {
        var normalized = NormalizeTemplateKey(templateKey);
        if (normalized is null)
        {
            throw new UnprocessableEntityException(["Template key is required."]);
        }

        return normalized;
    }

    private static void ValidateOrThrow(params object[] models)
    {
        var errors = new List<string>();

        foreach (var model in models)
        {
            errors.AddRange(DataAnnotationsValidator.Validate(model));
        }

        if (errors.Count > 0)
        {
            throw new UnprocessableEntityException(errors);
        }
    }
}

