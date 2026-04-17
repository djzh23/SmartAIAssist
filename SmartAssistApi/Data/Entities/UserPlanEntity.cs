using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("user_plan")]
public sealed class UserPlanEntity
{
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("plan")]
    public string Plan { get; set; } = "free";

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
