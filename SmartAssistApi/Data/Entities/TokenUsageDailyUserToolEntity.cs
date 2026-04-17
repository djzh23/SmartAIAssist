using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("token_usage_daily_user_tool")]
public sealed class TokenUsageDailyUserToolEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("usage_date", TypeName = "date")]
    public DateOnly UsageDate { get; set; }

    [Column("tool")]
    public string Tool { get; set; } = "";

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
