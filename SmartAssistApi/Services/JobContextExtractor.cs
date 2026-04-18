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

    private static readonly HashSet<string> TitleLineNoise =
    [
        "cookie", "cookies", "datenschutz", "impressum", "kontakt", "login", "anmelden",
        "barrierefreiheit", "rechtliche hinweise", "navigation", "footer", "bewerber",
        "bewerbungsfragen", "cookie einstellungen",
    ];

    public async Task<JobContext> ExtractAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Job posting input cannot be empty.", nameof(input));

        var jobText = input.Trim();
        JobPostingStructuredMeta? structuredMeta = null;
        if (LooksLikeUrl(jobText))
            (jobText, structuredMeta) = await FetchAndSanitizeHtmlAsync(jobText);

        if (jobText.Length < 100)
            throw new InvalidOperationException("Job posting text is too short to analyze.");

        var requirements = ExtractRequirements(jobText);
        var keywords = ExtractKeywords(jobText);

        var jobTitle = PickTitle(jobText, structuredMeta);
        var companyName = PickCompany(jobText, structuredMeta);
        var location = PickLocation(jobText, structuredMeta);

        return new JobContext
        {
            IsAnalyzed = true,
            JobTitle = jobTitle,
            CompanyName = companyName,
            Location = location,
            KeyRequirements = requirements.Count > 0
                ? requirements
                : new List<string> { "No explicit requirements detected. Use the full posting for tailoring." },
            Keywords = keywords.Count > 0
                ? keywords
                : new List<string> { "communication", "teamwork", "problem-solving" },
            RawJobText = jobText,
        };
    }

    private static string PickTitle(string jobText, JobPostingStructuredMeta? meta)
    {
        if (!string.IsNullOrWhiteSpace(meta?.Title))
            return meta.Title.Trim();
        return ExtractJobTitle(jobText);
    }

    private static string PickCompany(string jobText, JobPostingStructuredMeta? meta)
    {
        if (!string.IsNullOrWhiteSpace(meta?.CompanyName))
            return meta.CompanyName.Trim();
        return ExtractCompany(jobText);
    }

    private static string PickLocation(string jobText, JobPostingStructuredMeta? meta)
    {
        if (!string.IsNullOrWhiteSpace(meta?.Location))
            return meta.Location.Trim();
        return ExtractLocation(jobText);
    }

    private static bool LooksLikeUrl(string input) =>
        input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private async Task<(string Text, JobPostingStructuredMeta? Meta)> FetchAndSanitizeHtmlAsync(string url)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var html = await client.GetStringAsync(url);
            var result = JobPostingPageTextExtractor.FromHtmlWithMeta(html);
            var normalized = NormalizeWhitespacePreserveNewlines(result.PlainText);

            if (normalized.Length > 5000)
                normalized = normalized[..5000];

            return (normalized, result.StructuredMeta);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load job posting URL: {Url}", url);
            throw new InvalidOperationException(
                $"Could not load job posting URL '{url}'. Paste the full job text instead.");
        }
    }

    /// <summary>Keine vollständige Kollabierung von Zeilenumbrüchen (hilft Fallback-Heuristiken).</summary>
    private static string NormalizeWhitespacePreserveNewlines(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        s = Regex.Replace(s, "\r\n?", "\n");
        s = Regex.Replace(s, "[ \t\f\v]+", " ");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    private static bool LooksLikeTitleCandidate(string line)
    {
        if (line.Length < 6 || line.Length > 200)
            return false;
        var lower = line.ToLowerInvariant();
        foreach (var n in TitleLineNoise)
        {
            if (lower.Contains(n, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static string ExtractJobTitle(string text)
    {
        var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 3)
            .Take(25)
            .ToList();

        var rolePattern =
            @"\b(engineer|developer|manager|analyst|designer|consultant|specialist|lead|director|intern|trainee|officer|architect|scientist|researcher|associate|" +
            @"marketing|kommunikation|vertrieb|referent|berater|beraterin|assistent|assistentin|buchhalter|controller|sachbearbeiter|mitarbeiter|mitarbeiterin|" +
            @"wirtschaftsprüfung|steuer|personal|hr|it|software|data|product|project|sales|account|executive)\b";

        var match = lines.FirstOrDefault(line =>
            Regex.IsMatch(line, rolePattern, RegexOptions.IgnoreCase));

        if (!string.IsNullOrWhiteSpace(match))
            return match;

        foreach (var line in lines)
        {
            if (!line.Contains('|', StringComparison.Ordinal))
                continue;
            var first = line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(first) && LooksLikeTitleCandidate(first) && first.Length >= 8)
                return first.Length > 160 ? first[..160].Trim() : first;
        }

        var firstLine = lines.FirstOrDefault();
        if (!string.IsNullOrEmpty(firstLine) && LooksLikeTitleCandidate(firstLine))
            return firstLine.Length > 180 ? firstLine[..180].Trim() : firstLine;

        return "Unknown Role";
    }

    private static string ExtractCompany(string text)
    {
        var companyByAt = Regex.Match(text, @"\b(?:at|for)\s+([A-ZÄÖÜ][A-Za-z0-9&\-. ]{2,50})");
        if (companyByAt.Success)
            return companyByAt.Groups[1].Value.Trim();

        var bei = Regex.Match(text, @"(?i)\b(?:bei|unternehmen)[:\s]+([A-ZÄÖÜ][A-Za-z0-9&\-. ]{1,50})(?:\s|$|[,.])");
        if (bei.Success)
            return bei.Groups[1].Value.Trim();

        var companyLabel = Regex.Match(text, @"(?i)\bcompany[:\s]+([A-ZÄÖÜ][A-Za-z0-9&\-. ]{2,50})");
        if (companyLabel.Success)
            return companyLabel.Groups[1].Value.Trim();

        var pipeKarriere = Regex.Match(
            text,
            @"\|\s*Karriere\s*\|\s*([A-ZÄÖÜ0-9][A-Za-z0-9&\-.]{1,40})\s*\|");
        if (pipeKarriere.Success)
            return pipeKarriere.Groups[1].Value.Trim();

        var pipeCountry = Regex.Match(
            text,
            @"\|\s*([A-ZÄÖÜ][A-Za-z0-9&\-.]{1,40})\s*\|\s*(?:DE|AT|CH)\b");
        if (pipeCountry.Success)
            return pipeCountry.Groups[1].Value.Trim();

        return "Unknown Company";
    }

    private static string ExtractLocation(string text)
    {
        var locationLabel = Regex.Match(text, @"(?i)\blocation[:\s]+([A-Za-z0-9äöüÄÖÜ, \-()]{2,60})");
        if (locationLabel.Success)
            return locationLabel.Groups[1].Value.Trim();

        var standort = Regex.Match(text, @"(?i)\bstandort[:\s]+([A-Za-z0-9äöüÄÖÜ, \-()]{2,80})");
        if (standort.Success)
            return standort.Groups[1].Value.Trim().Split('|')[0].Trim();

        var remote = Regex.Match(text, @"(?i)\b(remote|hybrid|on[- ]?site|telearbeit|homeoffice)\b");
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
        var words = Regex.Matches(text.ToLowerInvariant(), @"\b[a-zäöü][a-z0-9äöü+#\-.]{2,}\b")
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
