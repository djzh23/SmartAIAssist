using System.Text.RegularExpressions;

namespace SmartAssistApi.Services;

/// <summary>Regelbasierte Kurz-Zusammenfassung gegen Wiederholungen in Job- und Interview-Chats.</summary>
public static class ConversationSummaryUpdater
{
    public const int MaxSummaryChars = 720;

    public static string MergeAfterTurn(string? previous, string primaryUserQuestion, string assistantReply)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(previous))
        {
            foreach (var line in previous.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (t.Length > 0)
                    lines.Add(t);
            }
        }

        var u = OneLine(primaryUserQuestion, 110);
        if (!string.IsNullOrWhiteSpace(u))
            lines.Add($"- Nutzer: {u}");

        foreach (Match m in Regex.Matches(assistantReply, @"^##\s+(.+)$", RegexOptions.Multiline))
        {
            var title = OneLine(m.Groups[1].Value, 80);
            if (!string.IsNullOrWhiteSpace(title))
                lines.Add($"- Abschnitt: {title}");
        }

        while (lines.Count > 14)
            lines.RemoveAt(0);

        var joined = string.Join("\n", lines);
        if (joined.Length <= MaxSummaryChars)
            return joined;

        return joined[^MaxSummaryChars..].TrimStart('\n', ' ', '-', '•');
    }

    private static string OneLine(string s, int max)
    {
        var t = s.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }
}
