using SmartAssistApi.Models;

namespace SmartAssistApi.Services.VectorStore;

public static class RetrievalStrategyFactory
{
    public static (string QueryText, RetrievalFilter Filter, int TopK) BuildStrategy(
        string toolType,
        string userMessage,
        SessionContext context)
    {
        var normalizedTool = string.IsNullOrWhiteSpace(toolType)
            ? "general"
            : toolType.Trim().ToLowerInvariant();

        return normalizedTool switch
        {
            "jobanalyzer" => (
                QueryText: $"{context.Job?.JobTitle ?? ""} {context.Job?.CompanyName ?? ""} {userMessage}".Trim(),
                Filter: new RetrievalFilter(ChunkType: "job_analysis"),
                TopK: 3),

            "interviewprep" => (
                QueryText: $"{context.InterviewJobTitle ?? ""} {context.InterviewCompany ?? ""} {userMessage}".Trim(),
                Filter: new RetrievalFilter(),
                TopK: 5),

            _ => (
                QueryText: userMessage.Trim(),
                Filter: new RetrievalFilter(),
                TopK: 4),
        };
    }
}
