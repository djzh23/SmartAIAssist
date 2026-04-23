namespace CvStudio.Domain.Entities;

public sealed class Snapshot
{
    /// <summary>
    /// Gets or sets the unique identifier of the snapshot.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the related resume.
    /// </summary>
    public Guid ResumeId { get; set; }

    /// <summary>
    /// Gets or sets the sequential version number per resume.
    /// </summary>
    public int VersionNumber { get; set; }

    /// <summary>
    /// Gets or sets the optional label assigned to this snapshot.
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets or sets the snapshot content serialized as JSON.
    /// </summary>
    public string ContentJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets the UTC creation timestamp of the snapshot.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the related resume navigation reference.
    /// </summary>
    public Resume? Resume { get; set; }
}
