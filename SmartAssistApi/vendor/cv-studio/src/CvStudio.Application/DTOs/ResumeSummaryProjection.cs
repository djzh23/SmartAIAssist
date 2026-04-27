namespace CvStudio.Application.DTOs;

/// <summary>
/// Lightweight projection for the resume list endpoint.
/// Populated via a raw SQL query that extracts profile fields from the JSONB column
/// at the database level — avoids loading and deserializing the full content JSON.
/// </summary>
public sealed class ResumeSummaryProjection
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TemplateKey { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? LinkedJobApplicationId { get; set; }
    public string? TargetCompany { get; set; }
    public string? TargetRole { get; set; }
    public string? Notes { get; set; }

    // Extracted at the DB level via JSONB operators — no full-document deserialisation needed.
    public string? ProfileFirstName { get; set; }
    public string? ProfileLastName { get; set; }
    public string? ProfileHeadline { get; set; }
    public string? ProfileEmail { get; set; }
    public string? ProfileLocation { get; set; }
}
