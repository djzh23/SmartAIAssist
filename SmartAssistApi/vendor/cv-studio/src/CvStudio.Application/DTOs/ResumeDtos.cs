using System.ComponentModel.DataAnnotations;
using CvStudio.Application.Contracts;

namespace CvStudio.Application.DTOs;

public sealed class ResumeDto
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    public string? TemplateKey { get; set; }

    [Required]
    public ResumeData ResumeData { get; set; } = new();

    public DateTime UpdatedAtUtc { get; set; }

    public string? LinkedJobApplicationId { get; set; }
    public string? TargetCompany { get; set; }
    public string? TargetRole { get; set; }
    public string? Notes { get; set; }
}

public sealed class ResumeSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? TemplateKey { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? LinkedJobApplicationId { get; set; }
    public string? TargetCompany { get; set; }
    public string? TargetRole { get; set; }
    public string? Notes { get; set; }
}

public sealed class ResumeVersionDto
{
    public Guid Id { get; set; }
    public Guid ResumeId { get; set; }
    public int VersionNumber { get; set; }
    public string? Label { get; set; }
    public ResumeData ResumeData { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class ResumeTemplateDto
{
    [Required]
    public string Key { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;
}

public sealed class CreateResumeRequest
{
    [Required]
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    public string? TemplateKey { get; set; }

    [Required]
    public ResumeData ResumeData { get; set; } = new();
}

public sealed class UpdateResumeRequest
{
    [Required]
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;

    public string? TemplateKey { get; set; }

    [Required]
    public ResumeData ResumeData { get; set; } = new();
}

public sealed class CreateVersionRequest
{
    [MaxLength(120)]
    public string? Label { get; set; }
}

public sealed class UpdateVersionRequest
{
    [MaxLength(120)]
    public string? Label { get; set; }
}

public sealed class LinkJobApplicationRequest
{
    /// <summary>Null to unlink. Must match an existing ApplicationId when set.</summary>
    [MaxLength(80)]
    public string? JobApplicationId { get; set; }

    [MaxLength(300)]
    public string? TargetCompany { get; set; }

    [MaxLength(300)]
    public string? TargetRole { get; set; }
}

public sealed class PatchResumeNotesRequest
{
    [MaxLength(2000)]
    public string? Notes { get; set; }
}

