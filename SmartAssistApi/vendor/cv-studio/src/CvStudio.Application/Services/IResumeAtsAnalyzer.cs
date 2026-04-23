using CvStudio.Application.Contracts;

namespace CvStudio.Application.Services;

public interface IResumeAtsAnalyzer
{
    AtsScoreResult Analyze(ResumeData resume, string jobDescription);
    AtsScoreResult Analyze(ResumeData resume, string jobDescription, JobCategory category);
    Task<AtsScoreResult> AnalyzeAsync(ResumeData resume, string jobDescription, JobCategory category, CancellationToken cancellationToken = default);
}
