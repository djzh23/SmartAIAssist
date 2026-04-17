namespace SmartAssistApi.Models;

/// <summary>
/// An insight the model can reuse in later chats (stored per user; Redis or Postgres per deployment).
/// </summary>
public class LearningInsight
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Category { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? SourceTool { get; set; }
    public string? SourceContext { get; set; }
    public string? JobApplicationId { get; set; }
    public string? TargetJobId { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public class UserLearningMemory
{
    public string UserId { get; set; } = string.Empty;
    public List<LearningInsight> Insights { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
