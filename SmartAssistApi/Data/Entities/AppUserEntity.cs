using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmartAssistApi.Data.Entities;

[Table("app_users")]
public sealed class AppUserEntity
{
    [Key]
    [Column("clerk_user_id")]
    public string ClerkUserId { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
