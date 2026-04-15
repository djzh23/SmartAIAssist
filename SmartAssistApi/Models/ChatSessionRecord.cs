using System.Text.Json.Serialization;

namespace SmartAssistApi.Models;

/// <summary>Matches frontend <c>ApiChatSessionRecord</c> (camelCase JSON).</summary>
public class ChatSessionRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("toolType")]
    public string ToolType { get; set; } = "general";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("lastMessageAt")]
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("messageCount")]
    public int MessageCount { get; set; }
}
