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

    public Task UpdateAsync(Resume resume, CancellationToken cancellationToken = default)
    {
        _dbContext.Resumes.Update(resume);
        return Task.CompletedTask;
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

