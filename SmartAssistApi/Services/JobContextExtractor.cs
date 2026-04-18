using System.Text.RegularExpressions;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public class JobContextExtractor(IHttpClientFactory httpClientFactory, ILogger<JobContextExtractor> logger)
    : IJobContextExtractor
{
    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "you", "your", "our", "are", "this", "that", "will",
        "from", "have", "has", "not", "all", "can", "job", "role", "team", "work", "years",
        "experience", "required", "preferred", "skills", "ability"
    ];

    public async Task<JobContext> ExtractAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Job posting input cannot be empty.", nameof(input));

        var jobText = input.Trim();
        if (LooksLikeUrl(jobText))
            jobText = await FetchAndSanitizeHtmlAsync(jobText);

        if (jobText.Length < 100)
            throw new InvalidOperationException("Job posting text is too short to analyze.");

        var requirements = ExtractRequirements(jobText);
        var keywords = ExtractKeywords(jobText);

        return new JobContext
        {
            IsAnalyzed = true,
            JobTitle = ExtractJobTitle(jobText),
            CompanyName = ExtractCompany(jobText),
            Location = ExtractLocation(jobText),
            KeyRequirements = requirements.Count > 0
                ? requirements
                : new List<string> { "No explicit requirements detected. Use the full posting for tailoring." },
            Keywords = keywords.Count > 0
                ? keywords
                : new List<string> { "communication", "teamwork", "problem-solving" },
            RawJobText = jobText,
        };
    }

    private static bool LooksLikeUrl(string input) =>
        input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private async Task<string> FetchAndSanitizeHtmlAsync(string url)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var html = await client.GetStringAsync(url);
            var withoutTags = Regex.Replace(html, "<[^>]+>", " ");
            var normalized = Regex.Replace(withoutTags, @"\s+", " ").Trim();

            if (normalized.Length > 5000)
                normalized = normalized[..5000];

            return normalized;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load job posting URL: {Url}", url);
            throw new InvalidOperationException(
                $"Could not load job posting URL '{url}'. Paste the full job text instead.");
        }
    }

    private static string ExtractJobTitle(string text)
    {
        var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 3)
            .Take(20)
            .ToList();

        var match = lines.FirstOrDefault(line =>
            Regex.IsMatch(line, @"\b(engineer|developer|manager|analyst|designer|consultant|specialist|lead)\b",
                RegexOptions.IgnoreCase));

        return string.IsNullOrWhiteSpace(match) ? "Unknown Role" : match;
    }

    private static string ExtractCompany(string text)
    {
        var companyByAt = Regex.Match(text, @"\b(?:at|for)\s+([A-Z][A-Za-z0-9&\-. ]{2,40})");
        if (companyByAt.Success)
            return companyByAt.Groups[1].Value.Trim();

        var companyLabel = Regex.Match(text, @"(?i)\bcompany[:\s]+([A-Z][A-Za-z0-9&\-. ]{2,40})");
        if (companyLabel.Success)
            return companyLabel.Groups[1].Value.Trim();

        return "Unknown Company";
    }

    private static string ExtractLocation(string text)
    {
        var locationLabel = Regex.Match(text, @"(?i)\blocation[:\s]+([A-Za-z0-9, \-()]{2,60})");
        if (locationLabel.Success)
            return locationLabel.Groups[1].Value.Trim();

        var remote = Regex.Match(text, @"(?i)\b(remote|hybrid|on[- ]?site)\b");
        if (remote.Success)
            return remote.Value.Trim();

        return "Not specified";
    }

    private static List<string> ExtractRequirements(string text)
    {
        var lines = text.Split(['\n', '\r', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 12)
            .Where(l => Regex.IsMatch(l, @"(?i)\b(must|required|experience|proficient|skills|knowledge|familiar)\b"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return lines;
    }

    private static List<string> ExtractKeywords(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\b[a-z][a-z0-9+#\-.]{2,}\b")
            .Select(m => m.Value)
            .Where(w => !StopWords.Contains(w))
            .Where(w => w.Length <= 24)
            .GroupBy(w => w)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(20)
            .Select(g => g.Key)
            .ToList();

        return words;
    }
}
