using CvStudio.Application.Contracts;

namespace CvStudio.Application.Services;

public sealed class ResumeAtsAnalyzer : IResumeAtsAnalyzer
{
    private readonly IAtsScoreService _atsScoreService;

    public ResumeAtsAnalyzer(IAtsScoreService atsScoreService)
    {
        _atsScoreService = atsScoreService;
    }

    public AtsScoreResult Analyze(ResumeData resume, string jobDescription)
    {
        return _atsScoreService.Calculate(resume, jobDescription);
    }

    public AtsScoreResult Analyze(ResumeData resume, string jobDescription, JobCategory category)
    {
        return _atsScoreService.Calculate(resume, jobDescription, category);
    }

    public Task<AtsScoreResult> AnalyzeAsync(ResumeData resume, string jobDescription, JobCategory category, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_atsScoreService.Calculate(resume, jobDescription, category));
    }
}
