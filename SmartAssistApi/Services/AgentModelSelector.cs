using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Services;

/// <summary>
/// Default routing: Haiku 4.5 for all tools (cost). Sonnet is opt-in per tool via
/// <c>Anthropic:SonnetTools</c> (comma-separated tool types, or <c>*</c> for all).
/// </summary>
public static class AgentModelSelector
{
    public const string DefaultHaikuModelId = "claude-haiku-4-5-20251001";
    public const string DefaultSonnetModelId = "claude-sonnet-4-5-20250929";

    /// <summary>
    /// Legacy helper: Haiku is the default for every tool; Sonnet is only used when listed in
    /// <c>Anthropic:SonnetTools</c> (see <see cref="ResolveModel"/>).
    /// </summary>
    public static bool UsesHaikuModel(string toolType) => true;

    /// <summary>
    /// Resolves model id. Haiku via <c>Anthropic:HaikuModel</c> unless tool is in
    /// <c>Anthropic:SonnetTools</c>, then <c>Anthropic:Model</c> / Sonnet default.
    /// </summary>
    public static string ResolveModel(string toolType, IConfiguration configuration)
    {
        var t = NormalizeToolType(toolType);
        if (ShouldUseSonnet(t, configuration))
        {
            var sonnet = configuration["Anthropic:Model"];
            return string.IsNullOrWhiteSpace(sonnet) ? DefaultSonnetModelId : sonnet.Trim();
        }

        var haiku = configuration["Anthropic:HaikuModel"];
        return string.IsNullOrWhiteSpace(haiku) ? DefaultHaikuModelId : haiku.Trim();
    }

    private static string NormalizeToolType(string? toolType) =>
        string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.Trim().ToLowerInvariant();

    private static bool ShouldUseSonnet(string normalizedToolType, IConfiguration configuration)
    {
        var raw = configuration["Anthropic:SonnetTools"];
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            if (p == "*")
                return true;
            if (string.Equals(p, normalizedToolType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
