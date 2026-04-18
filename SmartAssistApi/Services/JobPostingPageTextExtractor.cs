using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartAssistApi.Services;

/// <summary>
/// Extrahiert lesbaren Stellentext aus Roh-HTML (z. B. nach URL-Fetch).
/// Nutzt schema.org JSON-LD (JobPosting) wenn vorhanden, sonst bereinigtes Flächentext-Snippet ohne Style/Script/CSS.
/// </summary>
internal static class JobPostingPageTextExtractor
{
    private const int MaxChars = 5000;
    private const int MinPreferJsonLd = 120;

    private static readonly Regex ScriptBlock = new(
        @"<script\b[^>]*>.*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex StyleBlock = new(
        @"<style\b[^>]*>.*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex NoscriptBlock = new(
        @"<noscript\b[^>]*>.*?</noscript>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlComment = new(
        @"<!--.*?-->",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HtmlTag = new(
        @"<[^>]+>",
        RegexOptions.Compiled);

    /// <summary>Erlaubt z. B. type="application/ld+json;charset=utf-8".</summary>
    private static readonly Regex JsonLdScript = new(
        @"<script\b[^>]*type\s*=\s*[""']application/ld\+json[^""']*[""'][^>]*>(.*?)</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Primär JSON-LD JobPosting, sonst Body-Text ohne Script/Style.</summary>
    public static string FromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        if (TryExtractBestJobPostingPlainText(html, out var fromLd) && fromLd.Length >= MinPreferJsonLd)
            return Truncate(fromLd, MaxChars);

        var stripped = StripHtmlDocumentToPlainText(html);
        return Truncate(stripped, MaxChars);
    }

    private static string StripHtmlDocumentToPlainText(string html)
    {
        var s = html;
        s = HtmlComment.Replace(s, " ");
        s = ScriptBlock.Replace(s, " ");
        s = StyleBlock.Replace(s, " ");
        s = NoscriptBlock.Replace(s, " ");
        s = HtmlTag.Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static bool TryExtractBestJobPostingPlainText(string html, out string best)
    {
        best = string.Empty;
        var matches = JsonLdScript.Matches(html);
        foreach (Match m in matches)
        {
            var json = m.Groups.Count > 1 ? m.Groups[1].Value.Trim() : string.Empty;
            if (json.Length < 30)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (TryCollectJobPostingPlainText(doc.RootElement, out var text) && text.Length > best.Length)
                    best = text;
            }
            catch (JsonException)
            {
                // nächster Block
            }
        }

        return best.Length >= MinPreferJsonLd;
    }

    private static bool TryCollectJobPostingPlainText(JsonElement root, out string text)
    {
        text = string.Empty;
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in root.EnumerateArray())
                {
                    if (TryCollectJobPostingPlainText(el, out var t) && t.Length > text.Length)
                        text = t;
                }
                return text.Length > 0;
            case JsonValueKind.Object:
                if (IsJobPostingElement(root))
                {
                    text = BuildPlainTextFromJobPosting(root);
                    return text.Length > 0;
                }

                if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in graph.EnumerateArray())
                    {
                        if (TryCollectJobPostingPlainText(el, out var t) && t.Length > text.Length)
                            text = t;
                    }
                }

                return text.Length > 0;
            default:
                return false;
        }
    }

    private static bool IsJobPostingElement(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty("@type", out var typeEl))
            return false;
        return IsJobPostingType(typeEl);
    }

    private static bool IsJobPostingType(JsonElement typeEl)
    {
        return typeEl.ValueKind switch
        {
            JsonValueKind.String => string.Equals(typeEl.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Array => typeEl.EnumerateArray().Any(e =>
                e.ValueKind == JsonValueKind.String
                && string.Equals(e.GetString(), "JobPosting", StringComparison.OrdinalIgnoreCase)),
            _ => false,
        };
    }

    private static string BuildPlainTextFromJobPosting(JsonElement job)
    {
        string? title = null;
        if (job.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
            title = titleEl.GetString();

        string? rawDescription = null;
        if (job.TryGetProperty("description", out var descEl))
        {
            rawDescription = descEl.ValueKind switch
            {
                JsonValueKind.String => descEl.GetString(),
                _ => null,
            };
        }

        var titlePart = WebUtility.HtmlDecode((title ?? string.Empty).Trim());
        var descPart = HtmlJobDescriptionToPlainText(rawDescription);

        if (string.IsNullOrWhiteSpace(descPart))
            return titlePart;
        if (string.IsNullOrWhiteSpace(titlePart))
            return descPart;
        return $"{titlePart}\n\n{descPart}";
    }

    /// <summary>HTML-Stellenbeschreibung (z. B. von XING) zu lesbarem Fließtext.</summary>
    private static string HtmlJobDescriptionToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var s = html;
        s = Regex.Replace(s, @"(?is)<\s*br\s*/?>", "\n");
        s = Regex.Replace(s, @"(?is)</\s*(p|div|h[1-6]|li|tr)\s*>", "\n");
        s = Regex.Replace(s, @"(?is)<\s*li\s*>", "• ");
        s = HtmlTag.Replace(s, " ");
        s = WebUtility.HtmlDecode(s);
        s = Regex.Replace(s, @"[ \t\f\v]+", " ");
        s = Regex.Replace(s, @"\r\n?", "\n");
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        return s.Trim();
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..max];
    }
}
