namespace CvStudio.Application.Services;

public interface IDocxExportService
{
    Task<byte[]> ExportAsync(string clerkUserId, Guid resumeId, Guid? versionId = null, CancellationToken cancellationToken = default);
}

