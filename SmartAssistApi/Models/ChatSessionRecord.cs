namespace SmartAssistApi.Models;

/// <summary>Chat session tab metadata stored under sessions:{userId}.</summary>
public sealed class ChatSessionRecord
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Neuer Chat";
    public string ToolType { get; set; } = "general";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
    public int MessageCount { get; set; }
}
