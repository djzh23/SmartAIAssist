using CvStudio.Application.Contracts;

namespace CvStudio.Application.Services;

public interface IAtsScoreService
{
    AtsScoreResult Calculate(ResumeData resume, string jobDescription);
    AtsScoreResult Calculate(ResumeData resume, string jobDescription, JobCategory category);
}
