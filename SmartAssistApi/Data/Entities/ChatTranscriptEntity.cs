using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("chat_transcripts")]
public sealed class ChatTranscriptEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("session_id")]
    public string SessionId { get; set; } = "";

    [Column("tool_type")]
    public string ToolType { get; set; } = "general";

    /// <summary>JSON array of messages (same shape as Redis transcript <c>messages</c>).</summary>
    [Column("messages_json", TypeName = "jsonb")]
    public string MessagesJson { get; set; } = "[]";

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
