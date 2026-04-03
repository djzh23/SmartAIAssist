using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class JobAnalyzerToolTests
{
    [Fact]
    public async Task AnalyzeJobAsync_PlainText_ReturnsSentinelWithJobText()
    {
        const string jobText = "Software Engineer at Acme Corp. Requirements: C#, .NET, SQL.";

        var result = await JobAnalyzerTool.AnalyzeJobAsync(jobText);

        Assert.StartsWith("JOB_ANALYSIS_REQUEST:", result);
        Assert.Contains(jobText, result);
    }

    [Fact]
    public async Task AnalyzeJobAsync_PlainText_SentinelContainsFullInput()
    {
        const string jobText = "Looking for a senior developer with 5+ years experience.";

        var result = await JobAnalyzerTool.AnalyzeJobAsync(jobText);

        var extracted = result["JOB_ANALYSIS_REQUEST:".Length..];
        Assert.Equal(jobText, extracted);
    }

    [Fact]
    public async Task AnalyzeJobAsync_InvalidUrl_ReturnsGermanErrorMessage()
    {
        var result = await JobAnalyzerTool.AnalyzeJobAsync("https://this-url-definitely-does-not-exist-xyz123.invalid/job");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.False(result.StartsWith("JOB_ANALYSIS_REQUEST:", StringComparison.Ordinal));
        Assert.Contains("URL", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildJobAnalysisPrompt_ContainsRequiredSections()
    {
        var prompt = JobAnalyzerTool.BuildJobAnalysisPrompt();

        Assert.Contains("Position Overview", prompt);
        Assert.Contains("CV Optimization Tips", prompt);
        Assert.Contains("Keywords", prompt);
        Assert.Contains("Quick Win", prompt);
    }
}
