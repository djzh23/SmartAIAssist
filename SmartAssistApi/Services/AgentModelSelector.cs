using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Services;

/// <summary>Selects the Claude model per tool: Haiku for lightweight chat tools, Sonnet (config default) for heavier tools.</summary>
public static class AgentModelSelector
{
    public const string DefaultHaikuModelId = "claude-haiku-4-5-20251001";
    public const string DefaultSonnetModelId = "claude-sonnet-4-5-20250929";

    /// <summary>Tools that use Haiku for cost efficiency (general chat + language learning).</summary>
    public static bool UsesHaikuModel(string toolType)
    {
        var t = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant();
        return t is "general" or "language";
    }

    /// <summary>
    /// Resolves model id from configuration.
    /// <see cref="UsesHaikuModel"/> tools: <c>Anthropic:HaikuModel</c> or <see cref="DefaultHaikuModelId"/>.
    /// Others: <c>Anthropic:Model</c> or <see cref="DefaultSonnetModelId"/>.
    /// </summary>
    public static string ResolveModel(string toolType, IConfiguration configuration)
    {
        if (UsesHaikuModel(toolType))
        {
            var haiku = configuration["Anthropic:HaikuModel"];
            return string.IsNullOrWhiteSpace(haiku) ? DefaultHaikuModelId : haiku.Trim();
        }

        var sonnet = configuration["Anthropic:Model"];
        return string.IsNullOrWhiteSpace(sonnet) ? DefaultSonnetModelId : sonnet.Trim();
    }
}
