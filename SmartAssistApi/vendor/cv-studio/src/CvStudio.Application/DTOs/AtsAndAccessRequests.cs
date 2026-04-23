using CvStudio.Application.Contracts;
using CvStudio.Application.Services;

namespace CvStudio.Application.DTOs;

public sealed class AtsAnalyzeRequest
{
    public ResumeData ResumeData { get; set; } = new();

    public string JobDescription { get; set; } = string.Empty;

    public JobCategory Category { get; set; } = JobCategory.Auto;
}

public sealed class AccessVerifyRequest
{
    public string? Passcode { get; set; }
}
