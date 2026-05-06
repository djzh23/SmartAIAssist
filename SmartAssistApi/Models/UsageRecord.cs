namespace SmartAssistApi.Models;

public sealed class UsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheCreationTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public string? Model { get; set; }

    public int? ResponseTimeMs { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
}

public sealed class UsageStats
{
    public int TurnsToday { get; set; }
    public int TurnsThisWeek { get; set; }
    public int TurnsThisMonth { get; set; }
    public int ActiveUsers7d { get; set; }
    public int RecentRecordsCount { get; set; }
}

public sealed class ActiveUserUsage
{
    public string UserId { get; set; } = string.Empty;
    public int TurnsCount { get; set; }
    public DateTime LastSeenAt { get; set; }
    public decimal TotalEstimatedCostUsd { get; set; }
}
