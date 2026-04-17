using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("token_usage_global_daily")]
public sealed class TokenUsageGlobalDailyEntity
{
    [Column("usage_date", TypeName = "date")]
    public DateOnly UsageDate { get; set; }

    [Column("message_count")]
    public long MessageCount { get; set; }

    [Column("input_tokens")]
    public long InputTokens { get; set; }

    [Column("output_tokens")]
    public long OutputTokens { get; set; }

    [Column("cost_usd", TypeName = "numeric(18,6)")]
    public decimal CostUsd { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
