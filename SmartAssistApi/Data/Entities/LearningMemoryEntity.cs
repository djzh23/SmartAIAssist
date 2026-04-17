using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("learning_memories")]
public sealed class LearningMemoryEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    /// <summary>Full user learning memory document JSON (camelCase, same shape as Redis).</summary>
    [Column("memory_json", TypeName = "jsonb")]
    public string MemoryJson { get; set; } = "{}";

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
