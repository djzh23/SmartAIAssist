namespace CvStudio.Domain.Entities;

public sealed class Resume
{
    /// <summary>
    /// Gets or sets the unique identifier of the resume.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Owner identity (e.g. Clerk <c>sub</c>) when hosted inside SmartAssist; standalone API uses a fixed tenant id from configuration.
    /// </summary>
    public string ClerkUserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the title of the resume.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional template key used to create this resume.
    /// </summary>
    public string? TemplateKey { get; set; }

    /// <summary>
    /// Gets or sets the current resume content serialized as JSON.
    /// </summary>
    public string CurrentContentJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets the UTC timestamp of the latest update.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets all saved snapshots for this resume.
    /// </summary>
    public ICollection<Snapshot> Versions { get; set; } = new List<Snapshot>();

    /// <summary>
    /// Optional reference to a job application (cross-context string FK — no DB-level constraint).
    /// </summary>
    public string? LinkedJobApplicationId { get; set; }

    /// <summary>Target company name — denormalized for display without a join.</summary>
    public string? TargetCompany { get; set; }

    /// <summary>Target role name — denormalized for display without a join.</summary>
    public string? TargetRole { get; set; }

    /// <summary>Internal working notes visible only to the owner (e.g. "Docker emphasized, Kubernetes dropped").</summary>
    public string? Notes { get; set; }
}
