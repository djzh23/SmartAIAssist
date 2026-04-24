namespace SmartAssistApi.Models;

public sealed class CvStudioPdfExportRowDto
{
    public Guid Id { get; init; }
    public Guid ResumeId { get; init; }
    public Guid? VersionId { get; init; }
    public string Design { get; init; } = "";
    public string FileLabel { get; init; } = "";
    public DateTime CreatedAtUtc { get; init; }
    public bool HasStoredFile { get; init; }
    public string? TargetCompany { get; init; }
    public string? TargetRole { get; init; }
}
