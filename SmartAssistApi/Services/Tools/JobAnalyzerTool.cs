using System.Text.RegularExpressions;

namespace SmartAssistApi.Services.Tools;

public static class JobAnalyzerTool
{
    private static readonly HttpClient Http = new();

    public static async Task<string> AnalyzeJobAsync(string input)
    {
        var jobText = input;

        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var html = await Http.GetStringAsync(input);
                jobText = Regex.Replace(html, "<[^>]+>", " ");
                jobText = Regex.Replace(jobText, @"\s+", " ").Trim();
                if (jobText.Length > 3000)
                    jobText = jobText[..3000];
            }
            catch
            {
                return "URL konnte nicht geladen werden. " +
                       "Bitte füge den Text der Stellenanzeige direkt ein.";
            }
        }

        return $"JOB_ANALYSIS_REQUEST:{jobText}";
    }

    public static string BuildJobAnalysisPrompt() =>
        """
        Analyze the job posting below and respond in the same language as the job posting.

        Structure your response EXACTLY like this:

        ## 📋 Position Overview
        - Role: [job title]
        - Company: [company name if visible]
        - Type: [full-time/part-time/remote etc]
        - Key requirements: [3-5 bullet points]

        ## 🎯 What They Really Want
        [2-3 sentences about the ideal candidate]

        ## 📄 CV Optimization Tips
        - [Specific tip 1 based on job requirements]
        - [Specific tip 2]
        - [Specific tip 3]
        - [Specific tip 4]

        ## 🔑 Keywords for your CV
        [List 8-10 keywords/phrases from the job posting that should appear in the CV — these are what ATS systems scan for]

        ## ⚡ Quick Win
        [One specific sentence the candidate should add to their profile/summary section that directly matches this job]
        """;
}
