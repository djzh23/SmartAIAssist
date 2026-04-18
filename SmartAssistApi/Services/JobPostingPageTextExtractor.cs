using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SmartAssistApi.Models;

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

    internal sealed record ExtractResult(string PlainText, JobPostingStructuredMeta? StructuredMeta);

    /// <summary>Primär JSON-LD JobPosting, sonst Body-Text ohne Script/Style.</summary>
    public static string FromHtml(string html) => FromHtmlWithMeta(html).PlainText;

    /// <summary>Fließtext plus strukturierte Meta aus JSON-LD (wenn erkannt).</summary>
    public static ExtractResult FromHtmlWithMeta(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return new ExtractResult(string.Empty, null);

        if (TryBestFromJsonLd(html, out var fromLd, out var meta) && fromLd.Length >= MinPreferJsonLd)
            return new ExtractResult(Truncate(fromLd, MaxChars), meta);

        var stripped = StripHtmlDocumentToPlainText(html);
        return new ExtractResult(Truncate(stripped, MaxChars), null);
    }

    private static bool TryBestFromJsonLd(string html, out string bestText, out JobPostingStructuredMeta? bestMeta)
    {
        bestText = string.Empty;
        bestMeta = null;
        foreach (Match m in JsonLdScript.Matches(html))
        {
            var json = m.Groups.Count > 1 ? m.Groups[1].Value.Trim() : string.Empty;
            if (json.Length < 30)
                continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var (text, meta) in EnumerateJobPostingCandidates(doc.RootElement))
                {
                    if (text.Length > bestText.Length)
                    {
                        bestText = text;
                        bestMeta = meta;
                    }
                }
            }
            catch (JsonException)
            {
                // nächster Block
            }
        }

        return bestText.Length >= MinPreferJsonLd;
    }

    private static IEnumerable<(string Text, JobPostingStructuredMeta Meta)> EnumerateJobPostingCandidates(JsonElement root)
    {
        switch (root.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var el in root.EnumerateArray())
                {
                    foreach (var pair in EnumerateJobPostingCandidates(el))
                        yield return pair;
                }
                yield break;
            case JsonValueKind.Object:
                if (IsJobPostingElement(root))
                {
                    var text = BuildPlainTextFromJobPosting(root);
                    var meta = BuildStructuredMetaFromJobPosting(root);
                    if (text.Length > 0)
                        yield return (text, meta);
                    yield break;
                }

                if (root.TryGetProperty("@graph", out var graph) && graph.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in graph.EnumerateArray())
                    {
                        foreach (var pair in EnumerateJobPostingCandidates(el))
                            yield return pair;
                    }
                }
                yield break;
            default:
                yield break;
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

    private static JobPostingStructuredMeta BuildStructuredMetaFromJobPosting(JsonElement job)
    {
        string? title = null;
        if (job.TryGetProperty("title", out var titleEl) && titleEl.ValueKind == JsonValueKind.String)
            title = WebUtility.HtmlDecode(titleEl.GetString()!.Trim());

        var company = ResolveHiringOrganizationName(job);
        var location = ResolveJobLocationText(job);

        return new JobPostingStructuredMeta(
            NullIfEmpty(title),
            NullIfEmpty(company),
            NullIfEmpty(location));
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? ResolveHiringOrganizationName(JsonElement job)
    {
        if (!job.TryGetProperty("hiringOrganization", out var ho))
            return null;
        return ParseOrganizationName(ho);
    }

    private static string? ParseOrganizationName(JsonElement ho)
    {
        switch (ho.ValueKind)
        {
            case JsonValueKind.String:
                return WebUtility.HtmlDecode(ho.GetString()!.Trim());
            case JsonValueKind.Object:
                if (ho.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    return WebUtility.HtmlDecode(n.GetString()!.Trim());
                break;
            case JsonValueKind.Array:
                foreach (var el in ho.EnumerateArray())
                {
                    var name = ParseOrganizationName(el);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
                break;
        }

        return null;
    }

    private static string? ResolveJobLocationText(JsonElement job)
    {
        if (job.TryGetProperty("jobLocation", out var jl))
        {
            var t = ParseJobLocationElement(jl);
            if (!string.IsNullOrWhiteSpace(t))
                return t;
        }

        if (job.TryGetProperty("employmentLocation", out var el))
        {
            var t = ParseJobLocationElement(el);
            if (!string.IsNullOrWhiteSpace(t))
                return t;
        }

        return null;
    }

    private static string? ParseJobLocationElement(JsonElement jl)
    {
        switch (jl.ValueKind)
        {
            case JsonValueKind.String:
                return WebUtility.HtmlDecode(jl.GetString()!.Trim());
            case JsonValueKind.Object:
                if (jl.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                    return WebUtility.HtmlDecode(name.GetString()!.Trim());
                if (jl.TryGetProperty("address", out var addr) && addr.ValueKind == JsonValueKind.Object)
                    return ParsePostalAddress(addr);
                break;
            case JsonValueKind.Array:
                foreach (var el in jl.EnumerateArray())
                {
                    var t = ParseJobLocationElement(el);
                    if (!string.IsNullOrWhiteSpace(t))
                        return t;
                }
                break;
        }

        return null;
    }

    private static string? ParsePostalAddress(JsonElement addr)
    {
        if (addr.TryGetProperty("addressLocality", out var loc) && loc.ValueKind == JsonValueKind.String)
            return WebUtility.HtmlDecode(loc.GetString()!.Trim());
        if (addr.TryGetProperty("streetAddress", out var st) && st.ValueKind == JsonValueKind.String)
            return WebUtility.HtmlDecode(st.GetString()!.Trim());
        if (addr.TryGetProperty("addressCountry", out var c) && c.ValueKind == JsonValueKind.String)
            return WebUtility.HtmlDecode(c.GetString()!.Trim());
        return null;
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

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..max];
    }
}
