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
    public int RagChunksRetrieved { get; set; }
    public decimal RagTopScore { get; set; }
    public int RagLatencyMs { get; set; }
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

public sealed class TokenSummary
{
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheCreationTokens { get; set; }
    public decimal CacheHitRate { get; set; }
    public decimal TotalEstimatedCostUsd { get; set; }
    public int TotalTurns { get; set; }
    public decimal AvgInputTokensPerTurn { get; set; }
    public decimal AvgOutputTokensPerTurn { get; set; }
    public decimal AvgResponseTimeMs { get; set; }
    public decimal GroqTurnPercent { get; set; }
}

public sealed class TokenByToolRow
{
    public string ToolType { get; set; } = string.Empty;
    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public sealed class TokenByModelRow
{
    public string Model { get; set; } = string.Empty;
    public int Turns { get; set; }
    public decimal CostUsd { get; set; }
}

public sealed class TokenDailyRow
{
    public string Date { get; set; } = string.Empty;
    public int Turns { get; set; }
    public long InputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public sealed class RagSummary
{
    public int TurnsWithRag { get; set; }
    public int TurnsWithoutRag { get; set; }
    public decimal AvgChunksRetrieved { get; set; }
    public decimal AvgTopScore { get; set; }
    public decimal AvgRagLatencyMs { get; set; }
    public decimal RagAdoptionPercent { get; set; }
}
