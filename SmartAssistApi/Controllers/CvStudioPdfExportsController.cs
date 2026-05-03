using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/cv-studio/pdf-exports")]
public sealed class CvStudioPdfExportsController(IAppUserContext userContext, CvStudioPdfExportService pdfExports) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CvStudioPdfExportRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required" });

        var (limit, used) = await pdfExports.GetQuotaAsync(userId, cancellationToken).ConfigureAwait(false);
        var rows = await pdfExports.ListAsync(userId, cancellationToken).ConfigureAwait(false);
        var dto = rows.Select(x => new CvStudioPdfExportRowDto
        {
            Id = x.Id,
            ResumeId = x.ResumeId,
            VersionId = x.VersionId,
            Design = x.Design,
            FileLabel = x.FileLabel,
            CreatedAtUtc = x.CreatedAt,
            HasStoredFile = !string.IsNullOrEmpty(x.StorageObjectPath),
            TargetCompany = x.TargetCompany,
            TargetRole = x.TargetRole,
        }).ToList();

        Response.Headers.Append("X-Cv-Pdf-Quota-Limit", limit.ToString());
        Response.Headers.Append("X-Cv-Pdf-Quota-Used", used.ToString());
        return Ok(dto);
    }

    [HttpDelete("{exportId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Delete(Guid exportId, CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        if (userContext.IsAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "auth_required" });

        var ok = await pdfExports.TryDeleteAsync(userId, exportId, cancellationToken).ConfigureAwait(false);
        if (!ok)
            return NotFound();
        return NoContent();
    }
}
