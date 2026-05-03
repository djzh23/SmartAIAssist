using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("app_users")]
public sealed class AppUserEntity
{
    [Key]
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("plan")]
    [MaxLength(20)]
    public string Plan { get; set; } = "free";

    [Column("plan_updated_at")]
    public DateTime? PlanUpdatedAt { get; set; }

    [Column("stripe_customer_id")]
    [MaxLength(255)]
    public string? StripeCustomerId { get; set; }

    [Column("onboarding_state")]
    [MaxLength(20)]
    public string OnboardingState { get; set; } = "pending";

    [Column("last_active_at")]
    public DateTime LastActiveAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
