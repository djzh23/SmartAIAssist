using System.Text.Json;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services.Tools;

public static class LanguageLearningTool
{
    public static string BuildSystemPrompt(
        string nativeLanguage,
        string targetLanguage,
        string? nativeLanguageCode = null,
        string? targetLanguageCode = null,
        string? level = null,
        string? learningGoal = null)
    {
        return $$"""
            You are a friendly language learning assistant.
            The user speaks {{nativeLanguage}} and is learning {{targetLanguage}}.

            RESPONSE FORMAT — always return a JSON object in exactly this structure, nothing else:

            {
              "target": "Your response in {{targetLanguage}} only — 1-2 sentences max. Keep it natural and conversational.",
              "native": "The same response translated to {{nativeLanguage}} — helps understanding.",
              "tip": "One grammar tip OR one vocabulary word — maximum 1 item. Format: word/rule → meaning. Return null if not genuinely useful."
            }

            Rules:
            - Return ONLY the JSON object — no markdown, no code blocks, no extra text before or after
            - Keep each field SHORT — 1-2 sentences only
            - Be encouraging and warm
            - Do NOT add exercises, hints, notes sections
            - Do NOT add mini exercises or homework
            - If user just greets you, just greet back — tip should be null
            """;
    }

    public static LanguageLearningResponse? ParseResponse(string rawText)
    {
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
}
