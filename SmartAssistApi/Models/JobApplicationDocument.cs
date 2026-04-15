using System.Text.Json.Serialization;

namespace SmartAssistApi.Models;

/// <summary>Stored JSON for one job application (aligned with React <c>JobApplicationApi</c>).</summary>
public class JobApplicationDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("jobTitle")]
    public string JobTitle { get; set; } = string.Empty;

    [JsonPropertyName("company")]
    public string Company { get; set; } = string.Empty;

    [JsonPropertyName("jobUrl")]
    public string? JobUrl { get; set; }

    [JsonPropertyName("jobDescription")]
    public string? JobDescription { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("statusUpdatedAt")]
    public DateTime StatusUpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tailoredCvNotes")]
    public string? TailoredCvNotes { get; set; }

    [JsonPropertyName("coverLetterText")]
    public string? CoverLetterText { get; set; }

    [JsonPropertyName("interviewNotes")]
    public string? InterviewNotes { get; set; }

    [JsonPropertyName("timeline")]
    public List<ApplicationTimelineEvent> Timeline { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("analysisSessionId")]
    public string? AnalysisSessionId { get; set; }

    [JsonPropertyName("interviewSessionId")]
    public string? InterviewSessionId { get; set; }
}

public class ApplicationTimelineEvent
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string? Note { get; set; }
}
