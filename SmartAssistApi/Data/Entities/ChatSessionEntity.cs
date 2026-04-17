using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("chat_sessions")]
public sealed class ChatSessionEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("session_id")]
    public string SessionId { get; set; } = "";

    [Column("title")]
    [MaxLength(120)]
    public string Title { get; set; } = "";

    [Column("tool_type")]
    [MaxLength(40)]
    public string ToolType { get; set; } = "general";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("last_message_at")]
    public DateTime LastMessageAt { get; set; }

    [Column("message_count")]
    public int MessageCount { get; set; }

    /// <summary>Sidebar order: ascending (0 = first).</summary>
    [Column("display_order")]
    public int DisplayOrder { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
