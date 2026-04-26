using Microsoft.EntityFrameworkCore;
using CvStudio.Application.Repositories;
using CvStudio.Domain.Entities;
using CvStudio.Infrastructure.Persistence;

namespace CvStudio.Infrastructure.Repositories;

public sealed class ResumeRepository : IResumeRepository
{
    private readonly CvStudioDbContext _dbContext;

    public ResumeRepository(CvStudioDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Resume>> ListAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Resumes
            .AsNoTracking()
            .Where(x => x.ClerkUserId == clerkUserId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<Resume?> GetByIdAsync(Guid id, string clerkUserId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Resumes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.ClerkUserId == clerkUserId, cancellationToken);
    }

    public async Task AddAsync(Resume resume, CancellationToken cancellationToken = default)
    {
        await _dbContext.Resumes.AddAsync(resume, cancellationToken);
    }

    /// <summary>
    /// Copies scalar fields onto the tracked entity if one with the same key is already in the change tracker
    /// (e.g. right after <see cref="AddAsync"/> + SaveChanges). Using <c>Update(detached)</c> would throw
    /// "another instance with the same key is already being tracked".
    /// </summary>
    public async Task UpdateAsync(Resume resume, CancellationToken cancellationToken = default)
    {
        var tracked = await _dbContext.Resumes
            .FirstOrDefaultAsync(x => x.Id == resume.Id && x.ClerkUserId == resume.ClerkUserId, cancellationToken)
            .ConfigureAwait(false);

        if (tracked is null)
            throw new InvalidOperationException($"Resume '{resume.Id}' was not found for update.");

        tracked.Title = resume.Title;
        tracked.TemplateKey = resume.TemplateKey;
        tracked.CurrentContentJson = resume.CurrentContentJson;
        tracked.UpdatedAtUtc = resume.UpdatedAtUtc;
        tracked.LinkedJobApplicationId = resume.LinkedJobApplicationId;
        tracked.TargetCompany = resume.TargetCompany;
        tracked.TargetRole = resume.TargetRole;
        tracked.Notes = resume.Notes;
    }

    public Task<int> DeleteAllAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Resumes.Where(x => x.ClerkUserId == clerkUserId).ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> DeleteByIdAsync(Guid id, string clerkUserId, CancellationToken cancellationToken = default)
    {
        return _dbContext.Resumes
            .Where(x => x.Id == id && x.ClerkUserId == clerkUserId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}

