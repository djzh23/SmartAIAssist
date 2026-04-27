namespace SmartAssistApi.Data.Entities;

public sealed class CvUserCategoryEntity
{
    public Guid Id { get; set; }
    public string ClerkUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
