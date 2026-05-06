using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("usage_records")]
public sealed class UsageRecordEntity
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("tool_type")]
    public string ToolType { get; set; } = string.Empty;

    [Column("session_id")]
    public string SessionId { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("input_tokens")]
    public int InputTokens { get; set; }

    [Column("output_tokens")]
    public int OutputTokens { get; set; }

    [Column("cache_creation_tokens")]
    public int CacheCreationTokens { get; set; }

    [Column("cache_read_tokens")]
    public int CacheReadTokens { get; set; }

    [Column("model")]
    public string? Model { get; set; }

    [Column("response_time_ms")]
    public int? ResponseTimeMs { get; set; }

    [Column("estimated_cost_usd", TypeName = "numeric(10,6)")]
    public decimal? EstimatedCostUsd { get; set; }

    [Column("rag_chunks_retrieved")]
    public int RagChunksRetrieved { get; set; }

    [Column("rag_top_score", TypeName = "numeric(5,4)")]
    public decimal RagTopScore { get; set; }

    [Column("rag_latency_ms")]
    public int RagLatencyMs { get; set; }
}
