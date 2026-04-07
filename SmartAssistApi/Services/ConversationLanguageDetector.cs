using System.Text.RegularExpressions;

namespace SmartAssistApi.Services;

public static class ConversationLanguageDetector
{
    private static readonly string[] GermanMarkers =
    [
        " der ", " die ", " das ", " und ", " ist ", " nicht ", " ich ", " wir ", " mit ",
        " für ", " stelle ", " bewerbung ", " lebenslauf ", " erfahrung ", " unternehmen ",
        " auf ", " eine ", " einen ", " kannst ", " bitte ", " deutsch ", " danke "
    ];

    private static readonly string[] EnglishMarkers =
    [
        " the ", " and ", " is ", " are ", " i ", " we ", " with ", " for ", " this ",
        " that ", " job ", " company ", " resume ", " experience ", " role ", " can you ",
        " please ", " english ", " thanks "
    ];

    public static string? DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = $" {Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim()} ";
        if (normalized.Length < 6)
            return null;

        if (LooksLikeCode(normalized))
            return null;

        var germanScore = GermanMarkers.Count(m => normalized.Contains(m, StringComparison.Ordinal));
        var englishScore = EnglishMarkers.Count(m => normalized.Contains(m, StringComparison.Ordinal));

        if (normalized.Contains('ä') || normalized.Contains('ö') || normalized.Contains('ü') || normalized.Contains('ß'))
            germanScore += 2;

        if (germanScore == englishScore)
            return null;

        return germanScore > englishScore ? "de" : "en";
    }

    private static bool LooksLikeCode(string text) =>
        text.Contains("```", StringComparison.Ordinal)
        || Regex.IsMatch(text, @"(?m)^\s*(public|private|class|function|def|const|let|var|if\s*\(|for\s*\(|while\s*\()");
}
