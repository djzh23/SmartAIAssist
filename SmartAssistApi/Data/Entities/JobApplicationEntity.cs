using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("job_applications")]
public sealed class JobApplicationEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("application_id")]
    [MaxLength(80)]
    public string ApplicationId { get; set; } = "";

    [Column("job_title")]
    [MaxLength(300)]
    public string JobTitle { get; set; } = "";

    [Column("company")]
    [MaxLength(300)]
    public string Company { get; set; } = "";

    [Column("job_url")]
    public string? JobUrl { get; set; }

    [Column("job_description")]
    public string? JobDescription { get; set; }

    [Column("status")]
    [MaxLength(80)]
    public string Status { get; set; } = "draft";

    [Column("status_updated_at")]
    public DateTime StatusUpdatedAt { get; set; }

    [Column("tailored_cv_notes")]
    public string? TailoredCvNotes { get; set; }

    [Column("cover_letter_text")]
    public string? CoverLetterText { get; set; }

    [Column("interview_notes")]
    public string? InterviewNotes { get; set; }

    /// <summary>PostgreSQL <c>jsonb</c> array of timeline events.</summary>
    [Column("timeline", TypeName = "jsonb")]
    public string TimelineJson { get; set; } = "[]";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [Column("analysis_session_id")]
    public string? AnalysisSessionId { get; set; }

    [Column("interview_session_id")]
    public string? InterviewSessionId { get; set; }
}
