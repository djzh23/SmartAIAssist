using System.Text.Json.Serialization;

namespace SmartAssistApi.Models;

/// <summary>
/// A job application card for one role. Redis key: applications:{userId} → JSON array.
/// </summary>
public sealed class JobApplication
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];

    public string JobTitle { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string? JobUrl { get; set; }
    public string? JobDescription { get; set; }

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Draft;
    public DateTime StatusUpdatedAt { get; set; } = DateTime.UtcNow;

    public string? TailoredCvNotes { get; set; }
    public string? CoverLetterText { get; set; }
    public string? InterviewNotes { get; set; }

    public List<ApplicationEvent> Timeline { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string? AnalysisSessionId { get; set; }
    public string? InterviewSessionId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApplicationStatus
{
    Draft,
    Applied,
    PhoneScreen,
    Interview,
    Assessment,
    Offer,
    Accepted,
    Rejected,
    Withdrawn,
}

public sealed class ApplicationEvent
{
    public DateTime Date { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? Note { get; set; }
}
