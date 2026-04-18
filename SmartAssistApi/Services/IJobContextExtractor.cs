using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Loads job posting context from a URL or pasted text.</summary>
public interface IJobContextExtractor
{
    Task<JobContext> ExtractAsync(string input);
}
