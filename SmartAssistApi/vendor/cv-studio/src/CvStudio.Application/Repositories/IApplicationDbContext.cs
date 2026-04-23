namespace CvStudio.Application.Repositories;

public interface IApplicationDbContext
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

