using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("user_usage_daily")]
public sealed class UserUsageDailyEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("usage_date", TypeName = "date")]
    public DateOnly UsageDate { get; set; }

    [Column("usage_count")]
    public int UsageCount { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
