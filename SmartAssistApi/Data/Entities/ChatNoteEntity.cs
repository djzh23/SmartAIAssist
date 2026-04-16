using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("chat_notes")]
public sealed class ChatNoteEntity
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = "";

    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("title")]
    [MaxLength(120)]
    public string Title { get; set; } = "";

    [Column("body")]
    public string Body { get; set; } = "";

    /// <summary>PostgreSQL <c>text[]</c>; mapped by Npgsql.</summary>
    [Column("tags")]
    public string[] Tags { get; set; } = [];

    [Column("source_json", TypeName = "jsonb")]
    public string? SourceJson { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
