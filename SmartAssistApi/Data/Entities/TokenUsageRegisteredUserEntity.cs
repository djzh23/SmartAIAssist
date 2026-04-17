using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("token_usage_registered_users")]
public sealed class TokenUsageRegisteredUserEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("first_seen_at")]
    public DateTime FirstSeenAt { get; set; }
}
