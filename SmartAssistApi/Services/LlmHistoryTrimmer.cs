using System.Collections;
using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Begrenzt den für das LLM sichtbaren Verlauf: letzte N Nachrichten, ältere Assistant-Turns gekürzt.
/// </summary>
public static class LlmHistoryTrimmer
{
    private const int AssistantHistorySnippetChars = 300;

    /// <summary>
    /// Maximale Anzahl Nachrichten (User+Assistant) für das Modell.
    /// </summary>
    public static int ResolveMaxMessages(string toolType, SessionContext context)
    {
        var t = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant();
        var richJob = string.Equals(t, "jobanalyzer", StringComparison.OrdinalIgnoreCase)
                      && context.Job is { IsAnalyzed: true };
        var richInterview = string.Equals(t, "interviewprep", StringComparison.OrdinalIgnoreCase)
                            && (!string.IsNullOrEmpty(context.InterviewJobTitle)
                                || !string.IsNullOrEmpty(context.UserCV));
        var hasSummary = !string.IsNullOrWhiteSpace(context.ConversationSummary);

        var maxPairs = t switch
        {
            var x when x == "jobanalyzer" || x == "interviewprep" => hasSummary ? 3 : 4,
            "programming" => 4,
            _ => 3,
        };

        if (richJob || richInterview)
            maxPairs = Math.Max(maxPairs, hasSummary ? 3 : 4);

        return maxPairs * 2;
    }

    public static List<Message> Trim(IReadOnlyList<Message> fullHistory, string toolType, SessionContext context)
    {
        var maxMessages = ResolveMaxMessages(toolType, context);
        var take = fullHistory.Count <= maxMessages
            ? fullHistory.ToList()
            : fullHistory.TakeLast(maxMessages).ToList();

        for (var i = 0; i < take.Count - 2; i++)
        {
            if (take[i].Role != RoleType.Assistant)
                continue;
            if (!TryGetPlainText(take[i], out var text) || text.Length <= AssistantHistorySnippetChars)
                continue;
            take[i] = new Message(RoleType.Assistant, text[..AssistantHistorySnippetChars].TrimEnd() + "…");
        }

        return take;
    }

    private static bool TryGetPlainText(Message msg, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;
        switch (msg.Content as object)
        {
            case string s:
                text = s;
                return true;
            case IEnumerable<ContentBase> blocks:
            {
                var parts = new List<string>();
                foreach (var block in blocks)
                {
                    if (block is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                        parts.Add(tc.Text);
                    else
                        return false;
                }

                if (parts.Count == 0)
                    return false;
                text = string.Join("", parts);
                return true;
            }
            default:
                return false;
        }
    }
}
