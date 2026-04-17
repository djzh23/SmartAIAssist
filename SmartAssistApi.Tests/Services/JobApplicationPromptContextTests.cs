using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class JobApplicationPromptContextTests
{
    [Fact]
    public void Build_ReturnsNull_WhenAppNull()
    {
        Assert.Null(JobApplicationPromptContext.Build(null));
    }

    [Fact]
    public void Build_ContainsJobTitleCompanyAndStatus()
    {
        var app = new JobApplicationDocument
        {
            Id = "abc123def456",
            JobTitle = "Engineer",
            Company = "Acme",
            Status = "draft",
            StatusUpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var block = JobApplicationPromptContext.Build(app);
        Assert.NotNull(block);
        Assert.Contains("Engineer", block, StringComparison.Ordinal);
        Assert.Contains("Acme", block, StringComparison.Ordinal);
        Assert.Contains("draft", block, StringComparison.Ordinal);
        Assert.Contains("abc123def456", block, StringComparison.Ordinal);
    }
}
