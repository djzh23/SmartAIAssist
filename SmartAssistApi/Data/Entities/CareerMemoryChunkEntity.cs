using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;

namespace SmartAssistApi.Data.Entities;

[Table("career_memory")]
public sealed class CareerMemoryChunkEntity
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Column("source_tool")]
    [MaxLength(40)]
    public string SourceTool { get; set; } = string.Empty;

    [Column("chunk_type")]
    [MaxLength(40)]
    public string ChunkType { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("embedding")]
    public Vector Embedding { get; set; } = new(new float[384]);

    [Column("session_id")]
    [MaxLength(64)]
    public string? SessionId { get; set; }

    [Column("job_title")]
    [MaxLength(200)]
    public string? JobTitle { get; set; }

    [Column("company")]
    [MaxLength(200)]
    public string? Company { get; set; }

    [Column("job_application_id")]
    [MaxLength(64)]
    public string? JobApplicationId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("content_hash")]
    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;
}
