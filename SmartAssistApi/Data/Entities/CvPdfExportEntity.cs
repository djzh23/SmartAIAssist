using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("cv_pdf_exports")]
public sealed class CvPdfExportEntity
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("clerk_user_id")]
    [MaxLength(128)]
    public string ClerkUserId { get; set; } = "";

    [Column("resume_id")]
    public Guid ResumeId { get; set; }

    [Column("version_id")]
    public Guid? VersionId { get; set; }

    [Column("design")]
    [MaxLength(8)]
    public string Design { get; set; } = "A";

    [Column("file_label")]
    public string FileLabel { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("storage_object_path")]
    public string? StorageObjectPath { get; set; }

    [Column("target_company")]
    [MaxLength(300)]
    public string? TargetCompany { get; set; }

    [Column("target_role")]
    [MaxLength(300)]
    public string? TargetRole { get; set; }
}
