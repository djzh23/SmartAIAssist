namespace SmartAssistApi.Models;

/// <summary>User-saved chat reply (Redis-backed, synced across devices).</summary>
public sealed class ChatNoteRecord
{
    public string Id { get; set; } = "";

    public string CreatedAt { get; set; } = "";

    public string UpdatedAt { get; set; } = "";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public List<string> Tags { get; set; } = [];

    public ChatNoteSource? Source { get; set; }
}

public sealed class ChatNoteSource
{
    public string ToolType { get; set; } = "";

    public string SessionId { get; set; } = "";

    public string MessageId { get; set; } = "";
}
