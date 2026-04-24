using Microsoft.AspNetCore.Mvc;
using CvStudio.Application.DTOs;
using CvStudio.Application.Services;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/cv-studio/resumes")]
public sealed class CvStudioResumesController(
    ClerkAuthService clerkAuth,
    IResumeService resumeService,
    ISnapshotService snapshotService,
    IPdfExportService pdfExportService,
    IDocxExportService docxExportService,
    CvStudioPdfExportService pdfExportTracker,
    ILogger<CvStudioResumesController> logger) : ControllerBase
{
    private const string PdfContentType = "application/pdf";
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string PdfDesignBShort = "B";
    private const string PdfDesignCShort = "C";
    private const string PdfDesignBName = "DESIGNB";
    private const string PdfDesignCName = "DESIGNC";

    private (string userId, IActionResult? unauthorized) RequireUser()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return (string.Empty, Unauthorized(new { error = "auth_required" }));
        return (userId, null);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create([FromBody] CreateResumeRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await resumeService.CreateAsync(uid, request, cancellationToken);
        return CreatedAtAction(nameof(GetCurrent), new { id = resume.Id }, resume);
    }

    [HttpPost("templates/{templateKey}")]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateFromTemplate(
        string templateKey,
        [FromBody] CreateFromTemplateBody? body,
        CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await resumeService.CreateFromTemplateAsync(uid, templateKey, body?.Link, cancellationToken);
        return CreatedAtAction(nameof(GetCurrent), new { id = resume.Id }, resume);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ResumeSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resumes = await resumeService.ListAsync(uid, cancellationToken);
        return Ok(resumes);
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        await resumeService.DeleteAllAsync(uid, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteOne(Guid id, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        await pdfExportTracker.DeleteExportsForResumeAsync(uid, id, cancellationToken).ConfigureAwait(false);
        await resumeService.DeleteAsync(uid, id, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrent(Guid id, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await resumeService.GetCurrentAsync(uid, id, cancellationToken);
        return Ok(resume);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateResumeRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await resumeService.UpdateAsync(uid, id, request, cancellationToken);
        return Ok(resume);
    }

    [HttpPatch("{id:guid}/link-application")]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LinkApplication(Guid id, [FromBody] LinkJobApplicationRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await resumeService.LinkJobApplicationAsync(uid, id, request, cancellationToken);
        return Ok(resume);
    }

    [HttpPatch("{id:guid}/notes")]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> PatchNotes(Guid id, [FromBody] PatchResumeNotesRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await resumeService.PatchNotesAsync(uid, id, request, cancellationToken);
        return Ok(resume);
    }

    [HttpPost("{id:guid}/versions")]
    [ProducesResponseType(typeof(ResumeVersionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateVersion(Guid id, [FromBody] CreateVersionRequest? request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var normalized = request ?? new CreateVersionRequest();
        var version = await snapshotService.CreateSnapshotAsync(uid, id, normalized, cancellationToken);
        return CreatedAtAction(nameof(GetVersion), new { id, versionId = version.Id }, version);
    }

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType(typeof(IReadOnlyList<ResumeVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListVersions(Guid id, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var versions = await snapshotService.ListSnapshotsAsync(uid, id, cancellationToken);
        return Ok(versions);
    }

    [HttpGet("{id:guid}/versions/{versionId:guid}")]
    [ProducesResponseType(typeof(ResumeVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetVersion(Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var version = await snapshotService.GetSnapshotAsync(uid, id, versionId, cancellationToken);
        return Ok(version);
    }

    [HttpPut("{id:guid}/versions/{versionId:guid}")]
    [ProducesResponseType(typeof(ResumeVersionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateVersion(Guid id, Guid versionId, [FromBody] UpdateVersionRequest request, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var version = await snapshotService.UpdateSnapshotAsync(uid, id, versionId, request, cancellationToken);
        return Ok(version);
    }

    [HttpDelete("{id:guid}/versions/{versionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteVersion(Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        await snapshotService.DeleteSnapshotAsync(uid, id, versionId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/versions/{versionId:guid}/restore")]
    [ProducesResponseType(typeof(ResumeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RestoreVersion(Guid id, Guid versionId, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var resume = await snapshotService.RestoreSnapshotToWorkingCopyAsync(uid, id, versionId, cancellationToken);
        return Ok(resume);
    }

    [HttpGet("{id:guid}/pdf")]
    [Produces(PdfContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadPdf(
        Guid id,
        [FromQuery] Guid? versionId,
        [FromQuery] string? design,
        [FromQuery] string? fileName,
        CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;

        var (limit, used) = await pdfExportTracker.GetQuotaAsync(uid, cancellationToken).ConfigureAwait(false);
        if (used >= limit)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "PDF-Export-Limit",
                Detail = $"Du hast das Limit von {limit} gespeicherten PDF-Exports erreicht ({used}/{limit}). Lösche einen Eintrag unter CV.Studio → PDF-Exports oder wähle einen höheren Tarif.",
                Instance = HttpContext.Request.Path,
            });
        }

        var parsedDesign = ParsePdfDesign(design);
        var pdf = await pdfExportService.ExportAsync(uid, id, versionId, parsedDesign, cancellationToken).ConfigureAwait(false);
        var downloadName = BuildCvPdfDownloadName(fileName, id, versionId);
        var designLetter = PdfDesignLetter(parsedDesign);

        CvPdfExportEntity row;
        try
        {
            row = await pdfExportTracker.RecordPdfExportAsync(
                    uid,
                    id,
                    versionId,
                    designLetter,
                    downloadName,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CvStudioPdfQuotaExceededException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "PDF-Export-Limit",
                Detail = ex.Message,
                Instance = HttpContext.Request.Path,
            });
        }

        logger.LogInformation("CV.Studio PDF for resume {ResumeId}, version {VersionId}, design {Design}, export {ExportId}", id, versionId, parsedDesign, row.Id);

        Response.Headers.Append("X-Cv-Pdf-Export-Id", row.Id.ToString("D"));
        Response.Headers.Append("X-Cv-Pdf-Quota-Limit", limit.ToString());
        Response.Headers.Append("X-Cv-Pdf-Quota-Used", (used + 1).ToString());
        return File(pdf, PdfContentType, downloadName);
    }

    /// <summary>Safe download + list label; optional user <paramref name="requested"/> stem (without or with .pdf).</summary>
    private static string BuildCvPdfDownloadName(string? requested, Guid resumeId, Guid? versionId)
    {
        static string Fallback(Guid rid, Guid? vid) =>
            vid.HasValue ? $"resume-{rid:D}-v{vid:D}.pdf" : $"resume-{rid:D}.pdf";

        if (string.IsNullOrWhiteSpace(requested))
            return Fallback(resumeId, versionId);

        var trimmed = requested.Trim();
        if (trimmed.Length > 180)
            trimmed = trimmed[..180];

        trimmed = Path.GetFileName(trimmed.Replace('\\', '_').Replace('/', '_'));
        foreach (var ch in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(ch, '_');

        if (trimmed.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        trimmed = trimmed.Trim('.', '_', ' ', '-');
        if (string.IsNullOrWhiteSpace(trimmed))
            return Fallback(resumeId, versionId);

        return $"{trimmed}.pdf";
    }

    [HttpGet("{id:guid}/docx")]
    [Produces(DocxContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadDocx(Guid id, [FromQuery] Guid? versionId, CancellationToken cancellationToken)
    {
        var (uid, denied) = RequireUser();
        if (denied is not null) return denied;
        var docx = await docxExportService.ExportAsync(uid, id, versionId, cancellationToken);
        var fileName = versionId.HasValue ? $"resume-{id}-v{versionId}.docx" : $"resume-{id}.docx";

        logger.LogInformation("CV.Studio DOCX for resume {ResumeId}, version {VersionId}", id, versionId);

        return File(docx, DocxContentType, fileName);
    }

    private static PdfDesign ParsePdfDesign(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PdfDesign.DesignA;
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            PdfDesignBShort => PdfDesign.DesignB,
            PdfDesignBName => PdfDesign.DesignB,
            PdfDesignCShort => PdfDesign.DesignC,
            PdfDesignCName => PdfDesign.DesignC,
            _ => PdfDesign.DesignA
        };
    }

    private static string PdfDesignLetter(PdfDesign d) =>
        d == PdfDesign.DesignB ? "B" : d == PdfDesign.DesignC ? "C" : "A";
}
