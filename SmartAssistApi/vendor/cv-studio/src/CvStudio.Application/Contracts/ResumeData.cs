using System.ComponentModel.DataAnnotations;

namespace CvStudio.Application.Contracts;

public sealed class ResumeData
{
    [Required]
    public ProfileData Profile { get; set; } = new();

    public List<WorkItemData> WorkItems { get; set; } = [];
    public List<EducationItemData> EducationItems { get; set; } = [];
    public List<ResumeProjectItem> Projects { get; set; } = [];
    public List<SkillGroupData> Skills { get; set; } = [];
    public List<string> Hobbies { get; set; } = [];

    /// <summary> Optionale Sektions-Ueberschriften fuer Exporte (Sprache / Wunschtext). </summary>
    public CvSectionTitleOverrides? SectionTitles { get; set; }

    /// <summary>
    /// Reihenfolge der Hauptspalten-Sektionen fuer PDF/DOCX (Schluessel siehe <see cref="CvStudioMainSectionOrder"/>).
    /// Leer = Standardreihenfolge.
    /// </summary>
    public List<string> ContentSectionOrder { get; set; } = [];
}

public sealed class ProfileData
{
    [Required]
    [MaxLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(80)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Headline { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Location { get; set; } = string.Empty;

    [MaxLength(500)]
    public string ProfileImageUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? GitHubUrl { get; set; }

    [MaxLength(500)]
    public string? LinkedInUrl { get; set; }

    [MaxLength(500)]
    public string? PortfolioUrl { get; set; }

    [MaxLength(300)]
    public string? WorkPermit { get; set; }

    [MaxLength(1600)]
    public string Summary { get; set; } = string.Empty;
}

public sealed class WorkItemData
{
    [Required]
    [MaxLength(120)]
    public string Company { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Role { get; set; } = string.Empty;

    [MaxLength(30)]
    public string StartDate { get; set; } = string.Empty;

    [MaxLength(30)]
    public string EndDate { get; set; } = string.Empty;

    [MaxLength(1600)]
    public string Description { get; set; } = string.Empty;

    public List<string> Bullets { get; set; } = [];
}

public sealed class EducationItemData
{
    [Required]
    [MaxLength(120)]
    public string School { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string Degree { get; set; } = string.Empty;

    [MaxLength(30)]
    public string StartDate { get; set; } = string.Empty;

    [MaxLength(30)]
    public string EndDate { get; set; } = string.Empty;

    [MaxLength(1600)]
    public string Description { get; set; } = string.Empty;
}

public sealed class SkillGroupData
{
    [Required]
    [MaxLength(80)]
    public string CategoryName { get; set; } = string.Empty;

    public List<string> Items { get; set; } = [];
}

public sealed class ResumeProjectItem
{
    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2400)]
    public string Description { get; set; } = string.Empty;

    public List<string> Technologies { get; set; } = [];
}
