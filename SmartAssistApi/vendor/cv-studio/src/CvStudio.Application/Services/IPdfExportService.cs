namespace CvStudio.Application.Services;

public interface IPdfExportService
{
    Task<byte[]> ExportAsync(string clerkUserId, Guid resumeId, Guid? versionId = null, PdfDesign design = PdfDesign.DesignA, CancellationToken cancellationToken = default);
}

