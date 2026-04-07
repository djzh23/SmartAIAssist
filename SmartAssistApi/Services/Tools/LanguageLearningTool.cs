using System.Text.Json;
using System.Text.RegularExpressions;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Services.Tools;

public static partial class LanguageLearningTool
{
    public static string BuildSystemPrompt(
        string nativeLanguage,
        string targetLanguage,
        string? nativeLanguageCode = null,
        string? targetLanguageCode = null,
        string? level = null,
        string? learningGoal = null)
    {
        _ = nativeLanguageCode;
        _ = targetLanguageCode;
        _ = level;
        _ = learningGoal;
        return SystemPromptBuilder.BuildLanguageLearningPrompt(nativeLanguage, targetLanguage);
    }

    public static LanguageLearningResponse? ParseResponse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var structured = ParseStructuredBlocks(rawText);
        if (structured is not null)
            return structured;

        try
        {
            var start = rawText.IndexOf('{');
            var end = rawText.LastIndexOf('}');
            if (start < 0 || end <= start) return null;

            var json = rawText[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new LanguageLearningResponse
            {
                TargetLanguageText = root.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "",
                NativeLanguageText = root.TryGetProperty("native", out var n) ? n.GetString() ?? "" : "",
                LearnTip = root.TryGetProperty("tip", out var tip) && tip.ValueKind != JsonValueKind.Null
                    ? tip.GetString()
                    : null
            };
        }
        catch
        {
            return null;
        }
    }

    private static LanguageLearningResponse? ParseStructuredBlocks(string text)
    {
        var targetMatch = ZielRegex().Match(text);
        if (!targetMatch.Success)
            return null;

        var targetText = targetMatch.Groups[1].Value.Trim();
        var translationMatch = UebersetzungRegex().Match(text);
        var translationText = translationMatch.Success ? translationMatch.Groups[1].Value.Trim() : "";
        var tipMatch = TippRegex().Match(text);
        var tipText = tipMatch.Success ? tipMatch.Groups[1].Value.Trim() : null;

        return new LanguageLearningResponse
        {
            TargetLanguageText = targetText,
            NativeLanguageText = translationText,
            LearnTip = string.IsNullOrWhiteSpace(tipText) ? null : tipText
        };
    }

    [GeneratedRegex(@"---ZIELSPRACHE---([\s\S]*?)---UEBERSETZUNG---", RegexOptions.IgnoreCase)]
    private static partial Regex ZielRegex();

    [GeneratedRegex(@"---UEBERSETZUNG---([\s\S]*?)(?:---TIPP---|---END---)", RegexOptions.IgnoreCase)]
    private static partial Regex UebersetzungRegex();

    [GeneratedRegex(@"---TIPP---([\s\S]*?)---END---", RegexOptions.IgnoreCase)]
    private static partial Regex TippRegex();
}
