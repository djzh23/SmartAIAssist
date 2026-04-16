namespace SmartAssistApi.Models;

/// <summary>
/// System prompt split for Anthropic fine-grained prompt caching:
/// <see cref="CachedPrefix"/> is marked with <c>cache_control</c>; the uncached block is
/// <see cref="UncachedSystemBlock"/> (variable tool context + language instructions).
/// </summary>
public sealed record SystemPromptParts(string CachedPrefix, string DynamicToolSuffix, string LanguageRule)
{
    /// <summary>Exact legacy text: tool prompt + blank line + language rule (matches former single-string <c>BuildPrompt</c>).</summary>
    public string ToCombinedPrompt()
    {
        if (string.IsNullOrWhiteSpace(CachedPrefix))
            throw new InvalidOperationException(
                "System prompt cached prefix is empty; refuse to call the model without static instructions.");

        if (string.IsNullOrWhiteSpace(LanguageRule))
            throw new InvalidOperationException(
                "System prompt language rule is empty; conversation language must be resolved before building the system prompt.");

        var tail = DynamicToolSuffix ?? string.Empty;
        return string.IsNullOrEmpty(tail)
            ? $"{CachedPrefix}\n\n{LanguageRule}"
            : $"{CachedPrefix}{tail}\n\n{LanguageRule}";
    }

    /// <summary>Second system block sent to Anthropic (not prompt-cached).</summary>
    public string UncachedSystemBlock =>
        string.IsNullOrEmpty(DynamicToolSuffix ?? string.Empty)
            ? LanguageRule
            : $"{DynamicToolSuffix}\n\n{LanguageRule}";

    /// <summary>Prepends career profile context before the rest of the uncached system block (not prompt-cached).</summary>
    public SystemPromptParts WithProfilePrefix(string profileContext)
    {
        if (string.IsNullOrWhiteSpace(profileContext))
            return this;

        var head = profileContext.Trim();
        var d = DynamicToolSuffix ?? string.Empty;
        var newDynamic = string.IsNullOrEmpty(d) ? head : $"{head}\n\n{d}";
        return this with { DynamicToolSuffix = newDynamic };
    }

    /// <summary>Prepends compact turn summary for anti-repetition (uncached; not in Redis profile cache).</summary>
    public SystemPromptParts WithConversationSummary(string? conversationSummary)
    {
        if (string.IsNullOrWhiteSpace(conversationSummary))
            return this;

        var block = $"""
            [BISHER_BEHANDELTE_THEMEN]
            {conversationSummary.Trim()}
            [ENDE_BISHER_BEHANDELTE_THEMEN]

            Regel: Diese Punkte nicht erneut ausführlich behandeln, außer der User verlangt Wiederholung oder Klärung.

            """;
        var d = DynamicToolSuffix ?? string.Empty;
        var newDynamic = string.IsNullOrEmpty(d) ? block.TrimEnd() : $"{block}{d}";
        return this with { DynamicToolSuffix = newDynamic };
    }
}
