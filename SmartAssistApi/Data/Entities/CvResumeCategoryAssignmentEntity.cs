namespace SmartAssistApi.Data.Entities;

public sealed class CvResumeCategoryAssignmentEntity
{
    public Guid ResumeId { get; set; }
    public string ClerkUserId { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
}
