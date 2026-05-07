using System.Text;
using System.Text.RegularExpressions;

namespace SmartAssistApi.Services;

/// <summary>
/// Builds a compact text snapshot from a full JobAnalyzer markdown reply so follow-up turns can reference
/// structured results without relying on trimmed chat history.
/// </summary>
public static class JobAnalysisSnapshotExtractor
{
    public const int MaxChars = 800;

    /// <summary>Returns null if the reply does not look like a complete first-pass analysis.</summary>
    public static string? TryBuild(string analysisReply)
    {
        if (string.IsNullOrWhiteSpace(analysisReply))
            return null;

        if (!LooksLikeCompleteAnalysis(analysisReply))
            return null;

        var sb = new StringBuilder();

        var decision = ExtractDecisionToken(analysisReply);
        if (!string.IsNullOrEmpty(decision))
            sb.AppendLine($"DECISION: {decision}");

        var bew = ExtractSectionBody(analysisReply, "Bewertung");
        if (!string.IsNullOrEmpty(bew))
        {
            var firstPara = FirstNonEmptyParagraph(bew);
            if (!string.IsNullOrEmpty(firstPara))
                sb.AppendLine($"BEWERTUNG: {OneLine(firstPara, 360)}");
        }

        var kwSection = ExtractSectionBody(analysisReply, "Keyword-Analyse");
        if (!string.IsNullOrEmpty(kwSection))
        {
            var missing = CollectKeywordRows(kwSection, preferMissing: true);
            var present = CollectKeywordRows(kwSection, preferMissing: false);
            if (missing.Count > 0)
                sb.AppendLine($"FEHLENDE_KEYWORDS: {string.Join(", ", missing.Take(14))}");
            if (present.Count > 0)
                sb.AppendLine($"VORHANDENE_KEYWORDS: {string.Join(", ", present.Take(14))}");
            if (missing.Count == 0 && present.Count == 0)
                sb.AppendLine($"KEYWORDS_KONTEXT: {OneLine(kwSection, 340)}");
        }

        var luecken = ExtractSectionBody(analysisReply, "Lücken-Analyse")
                      ?? ExtractSectionBody(analysisReply, "Lücken")
                      ?? ExtractSectionBody(analysisReply, "Luecken");
        if (!string.IsNullOrEmpty(luecken))
        {
            var compressed = Regex.Replace(luecken.Trim(), @"\s+", " ");
            if (compressed.Length > 340)
                compressed = compressed[..340] + "…";
            sb.AppendLine($"LÜCKEN: {compressed}");
        }

        var plan = ExtractSectionBody(analysisReply, "Sofort-Aktionsplan")
                   ?? ExtractSectionBody(analysisReply, "Aktionsplan");
        if (!string.IsNullOrEmpty(plan))
        {
            var steps = Regex.Matches(plan, @"(?m)^\s*\d+[\.\)]\s*(.+)$")
                .Select(m => OneLine(m.Groups[1].Value.Trim(), 140))
                .Where(s => s.Length > 8)
                .Take(3)
                .ToList();
            if (steps.Count > 0)
                sb.AppendLine($"AKTIONSPLAN: {string.Join(" | ", steps)}");
        }

        var result = sb.ToString().Trim();
        if (result.Length < 45)
            return null;

        if (result.Length > MaxChars)
            return result[..MaxChars].TrimEnd() + "…";

        return result;
    }

    private static bool LooksLikeCompleteAnalysis(string text)
    {
        static bool Has(string s, string needle) =>
            s.Contains(needle, StringComparison.OrdinalIgnoreCase);

        return Has(text, "## Bewertung")
               && Has(text, "## Keyword-Analyse")
               && (Has(text, "## Muss-Kriterien") || Has(text, "## Muss"))
               && (Has(text, "## Lücken") || Has(text, "## Luecken") || Has(text, "Lücken-Analyse"))
               && Has(text, "## Sofort-Aktionsplan");
    }

    private static string? ExtractDecisionToken(string text)
    {
        var m = Regex.Match(
            text,
            @"\[DECISION_PROMPT\]\s*:\s*(STARK|MÖGLICH|SCHWIERIG)",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static string? ExtractSectionBody(string text, string titleContains)
    {
        var pattern =
            $@"##\s*[^\n]*{Regex.Escape(titleContains)}[^\n]*\r?\n([\s\S]+?)(?=\r?\n##\s|\r?\n\[DECISION_PROMPT\]|\z)";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string FirstNonEmptyParagraph(string body)
    {
        foreach (var block in body.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None))
        {
            var t = block.Trim();
            if (t.Length > 0)
                return t;
        }

        var one = body.Trim();
        return one.Length > 0 ? one : string.Empty;
    }

    private static List<string> CollectKeywordRows(string keywordSection, bool preferMissing)
    {
        var results = new List<string>();
        foreach (var rawLine in keywordSection.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.Contains('|', StringComparison.Ordinal))
                continue;

            var compact = line.Replace(" ", "", StringComparison.Ordinal);
            if (compact.Contains("---|---", StringComparison.Ordinal) || Regex.IsMatch(compact, @"^\|[\-:|]+\|"))
                continue;

            var rowMissing =
                line.Contains('✗', StringComparison.Ordinal)
                || line.Contains("\u2717", StringComparison.Ordinal)
                || line.Contains('✘', StringComparison.Ordinal)
                || line.Contains("fehlend", StringComparison.OrdinalIgnoreCase);

            var rowPresent =
                line.Contains('✓', StringComparison.Ordinal)
                || line.Contains("\u2713", StringComparison.Ordinal)
                || line.Contains('✔', StringComparison.Ordinal)
                || Regex.IsMatch(line, @"\b(vorhanden)\b", RegexOptions.IgnoreCase);

            if (preferMissing && !rowMissing)
                continue;
            if (!preferMissing && !rowPresent)
                continue;

            var cells = line.Split('|').Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
            if (cells.Length < 2)
                continue;

            var kw = cells[0].Replace("**", "", StringComparison.Ordinal).Trim();
            kw = Regex.Replace(kw, @"\[|\]", "").Trim();
            if (kw.Length is <= 1 or >= 90)
                continue;
            if (kw.Equals("Keyword", StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(kw);
        }

        return results;
    }

    private static string OneLine(string s, int max)
    {
        var t = s.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }
}
