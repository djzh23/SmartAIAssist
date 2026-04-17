using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Builds the compact German profile block for the LLM system prompt.</summary>
public static class CareerProfileContextBuilder
{
    private const int MaxProfileContextChars = 1100;
    private const int MaxSkillsInContext = 8;
    private const int MaxCvRawExcerptChars = 280;
    private const int MaxCvSummaryChars = 360;
    private const int MaxTargetJobDescriptionChars = 420;
    private const int MaxExperienceLines = 3;
    private const int MaxExperienceLineChars = 130;

    public static string Build(CareerProfile profile, ProfileContextToggles toggles)
    {
        var body = new List<string>();

        if (toggles.IncludeBasicProfile)
        {
            if (!string.IsNullOrEmpty(profile.FieldLabel))
                body.Add($"Berufsfeld: {TruncateOneLine(profile.FieldLabel, 72)}");
            if (!string.IsNullOrEmpty(profile.LevelLabel))
                body.Add($"Seniorität: {TruncateOneLine(profile.LevelLabel, 72)}");
            if (!string.IsNullOrEmpty(profile.CurrentRole))
                body.Add($"Aktuelle Rolle: {TruncateOneLine(profile.CurrentRole, 90)}");
            if (profile.Goals.Count > 0)
            {
                var goals = string.Join(", ", profile.Goals.Take(4));
                body.Add($"Ziele: {TruncateOneLine(goals, 100)}");
            }
        }

        if (toggles.IncludeSkills && profile.Skills.Count > 0)
        {
            var slice = profile.Skills.Where(s => !string.IsNullOrWhiteSpace(s)).Take(MaxSkillsInContext).ToList();
            var line = $"Kernskills ({slice.Count}): {string.Join(", ", slice)}";
            if (profile.Skills.Count > MaxSkillsInContext)
                line += $" (+{profile.Skills.Count - MaxSkillsInContext} weitere im Profil, hier weglassen)";
            body.Add(TruncateOneLine(line, 220));
        }

        if (toggles.IncludeExperience && profile.Experience.Count > 0)
        {
            body.Add("Relevante Erfahrung:");
            foreach (var exp in profile.Experience.Take(MaxExperienceLines))
            {
                var line = $"• {exp.Title}".TrimEnd();
                if (!string.IsNullOrEmpty(exp.Company)) line += $" @ {exp.Company}";
                if (!string.IsNullOrEmpty(exp.Duration)) line += $" ({exp.Duration})";
                if (!string.IsNullOrEmpty(exp.Summary))
                {
                    var s = TruncateOneLine(exp.Summary.Replace('\n', ' '), 80);
                    line += $" — {s}";
                }

                body.Add(TruncateOneLine(line, MaxExperienceLineChars));
            }
        }

        if (toggles.IncludeCv)
        {
            if (!string.IsNullOrEmpty(profile.CvSummary))
                body.Add($"CV (Kurz): {TruncateOneLine(profile.CvSummary, MaxCvSummaryChars)}");
            else if (!string.IsNullOrEmpty(profile.CvRawText))
            {
                var raw = profile.CvRawText.Replace('\n', ' ').Trim();
                var n = Math.Min(MaxCvRawExcerptChars, raw.Length);
                body.Add($"CV (Auszug, gekürzt): {raw[..n]}");
            }
        }

        if (!string.IsNullOrEmpty(toggles.ActiveTargetJobId))
        {
            var targetJob = profile.TargetJobs.FirstOrDefault(j => j.Id == toggles.ActiveTargetJobId);
            if (targetJob is not null)
            {
                var line = $"Aktive Zielstelle: {targetJob.Title}";
                if (!string.IsNullOrEmpty(targetJob.Company)) line += $" bei {targetJob.Company}";
                body.Add(TruncateOneLine(line, 120));
                if (!string.IsNullOrEmpty(targetJob.Description))
                {
                    var d = targetJob.Description.Replace('\n', ' ').Trim();
                    var n = Math.Min(MaxTargetJobDescriptionChars, d.Length);
                    body.Add($"Stellenkontext: {d[..n]}");
                }
            }
        }

        if (body.Count == 0)
            return string.Empty;

        var parts = new List<string> { "[NUTZERPROFIL]" };
        parts.AddRange(body);

        var limits = BuildProfileLimitsBlock(profile);
        if (!string.IsNullOrEmpty(limits))
        {
            parts.Add("[GRENZEN]");
            parts.Add(limits);
            parts.Add("[ENDE GRENZEN]");
        }

        parts.Add("[ENDE NUTZERPROFIL]");

        var text = string.Join("\n", parts);
        if (text.Length > MaxProfileContextChars)
            text = text[..MaxProfileContextChars] + "\n[…]";

        return text;
    }

    private static string TruncateOneLine(string s, int max)
    {
        var t = s.Trim().Replace('\r', ' ').Replace('\n', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    private static string? BuildProfileLimitsBlock(CareerProfile profile)
    {
        var level = (profile.Level ?? string.Empty).Trim().ToLowerInvariant();
        var label = (profile.LevelLabel ?? string.Empty).ToLowerInvariant();
        var juniorish = level is "junior" or "entry" or "student" or "intern" or "werkstudent"
                        || label.Contains("junior", StringComparison.Ordinal)
                        || label.Contains("einsteiger", StringComparison.Ordinal)
                        || label.Contains("berufseinsteiger", StringComparison.Ordinal)
                        || label.Contains("werkstudent", StringComparison.Ordinal)
                        || label.Contains("praktikum", StringComparison.Ordinal);

        var lines = new List<string>();
        if (juniorish)
        {
            lines.Add("Einstieg/Junior-Level erkennbar — keine Senior- oder Lead-Behauptungen; Produktions-Ownership nur wenn belegt.");
        }
        else if (level.Contains("senior", StringComparison.Ordinal) || label.Contains("senior", StringComparison.Ordinal))
        {
            lines.Add("Senior-Level erkennbar — trotzdem keine erfundenen Metriken oder Teamgrößen.");
        }
        else
        {
            lines.Add("Nur aus den gelieferten Profilzeilen argumentieren — nichts hinzudichten.");
        }

        if (string.IsNullOrEmpty(profile.CvSummary) && string.IsNullOrEmpty(profile.CvRawText) && profile.Experience.Count == 0)
            lines.Add("Wenig strukturierte Werdegang-Daten — vorsichtige Formulierungen, Lücken offen nennen.");

        return lines.Count == 0 ? null : string.Join(" ", lines);
    }
}
