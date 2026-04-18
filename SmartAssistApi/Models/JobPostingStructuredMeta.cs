namespace SmartAssistApi.Models;

/// <summary>Strukturierte Felder aus schema.org JobPosting (JSON-LD), falls vorhanden.</summary>
public sealed record JobPostingStructuredMeta(
    string? Title,
    string? CompanyName,
    string? Location);
