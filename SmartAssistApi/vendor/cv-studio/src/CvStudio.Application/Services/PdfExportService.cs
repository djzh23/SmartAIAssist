using Microsoft.Extensions.Logging;
using CvStudio.Application.Exceptions;
using CvStudio.Application.Repositories;

namespace CvStudio.Application.Services;

public sealed class PdfExportService : IPdfExportService
{
    private readonly IResumeRepository _resumeRepository;
    private readonly ISnapshotRepository _versionRepository;
    private readonly IPdfGenerator _pdfGenerator;
    private readonly ILogger<PdfExportService> _logger;

    public PdfExportService(
        IResumeRepository resumeRepository,
        ISnapshotRepository versionRepository,
        IPdfGenerator pdfGenerator,
        ILogger<PdfExportService> logger)
    {
        _resumeRepository = resumeRepository;
        _versionRepository = versionRepository;
        _pdfGenerator = pdfGenerator;
        _logger = logger;
    }

    public async Task<byte[]> ExportAsync(string clerkUserId, Guid resumeId, Guid? versionId = null, PdfDesign design = PdfDesign.DesignA, CancellationToken cancellationToken = default)
    {
        try
        {
            var contentJson = await ResolveContentJsonAsync(clerkUserId, resumeId, versionId, cancellationToken);
            var bytes = _pdfGenerator.GenerateFromResumeJson(contentJson, design);

            if (bytes.Length == 0)
            {
                throw new InvalidOperationException("PDF generation produced no content.");
            }

            _logger.LogInformation("Generated PDF for resume {ResumeId}, version {VersionId}, design {Design}", resumeId, versionId, design);
            return bytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF generation failed for resume {ResumeId}, version {VersionId}, design {Design}", resumeId, versionId, design);
            throw;
        }
    }

    private async Task<string> ResolveContentJsonAsync(string clerkUserId, Guid resumeId, Guid? versionId, CancellationToken cancellationToken)
    {
        if (versionId.HasValue)
        {
            _ = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
                ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

            var version = await _versionRepository.GetByResumeAndVersionIdAsync(resumeId, versionId.Value, cancellationToken)
                ?? throw new NotFoundException($"Version '{versionId}' for resume '{resumeId}' was not found.");

            return version.ContentJson;
        }

        var resume = await _resumeRepository.GetByIdAsync(resumeId, clerkUserId, cancellationToken)
            ?? throw new NotFoundException($"Resume '{resumeId}' was not found.");

        return resume.CurrentContentJson;
    }
}
