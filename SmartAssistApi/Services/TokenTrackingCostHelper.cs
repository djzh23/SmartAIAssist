using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Shared pricing, sanitization, and display helpers for Redis and Postgres token tracking.</summary>
public static class TokenTrackingCostHelper
{
    private static readonly Dictionary<string, (decimal InputPerMillion, decimal OutputPerMillion)> ModelPricing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-haiku-4-5-20251001"] = (1.00m, 5.00m),
        ["claude-sonnet-4-20250514"] = (3.00m, 15.00m),
        ["claude-sonnet-4-5-20250929"] = (3.00m, 15.00m),
    };

    /// <inheritdoc cref="TokenTrackingService.CalculateCost"/>
    public static decimal CalculateCost(
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0)
    {
        if (cacheCreationInputTokens < 0 || cacheReadInputTokens < 0)
            throw new ArgumentOutOfRangeException(
                nameof(cacheCreationInputTokens),
                "Cache creation and cache read token counts must be non-negative.");

        var (inputPerM, outputPerM) = model.StartsWith("groq/", StringComparison.OrdinalIgnoreCase)
            ? (0m, 0m)
            : ModelPricing.GetValueOrDefault(model, (3.00m, 15.00m));
        var inputCost = (inputTokens / 1_000_000m) * inputPerM;
        var cacheWriteCost = (cacheCreationInputTokens / 1_000_000m) * inputPerM * 1.25m;
        var cacheReadCost = (cacheReadInputTokens / 1_000_000m) * inputPerM * 0.10m;
        var outputCost = (outputTokens / 1_000_000m) * outputPerM;
        return inputCost + cacheWriteCost + cacheReadCost + outputCost;
    }

    /// <summary>Safe Redis segment: keeps model ids readable (e.g. groq/llama-3.3-70b → groq_llama-3.3-70b).</summary>
    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        var b = new StringBuilder(value.Trim().Length);
        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
                b.Append(c);
            else if (c is '/' or ':')
                b.Append('_');
        }

        var s = b.ToString();
        return string.IsNullOrEmpty(s) ? "unknown" : s;
    }

    public static string InferProviderFromModelKey(string modelKey) =>
        modelKey.StartsWith("groq_", StringComparison.OrdinalIgnoreCase) ? "Groq" : "Anthropic";

    /// <summary>Redis stores legacy Groq $ from old pricing; dashboard treats Groq keys as 0 USD.</summary>
    public static decimal AdjustStoredCostUsdForDisplay(string redisModelKey, decimal storedCostUsd) =>
        redisModelKey.StartsWith("groq_", StringComparison.OrdinalIgnoreCase) ? 0m : storedCostUsd;

    /// <summary>Pick tool with highest tc_* count; ties broken by lexicographic tool name.</summary>
    public static string? ParseTopToolFromTcFields(Dictionary<string, string> map)
    {
        string? best = null;
        var bestCount = -1;
        const string prefix = "tc_";
        foreach (var kv in map)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (!int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                continue;
            var toolName = kv.Key.Length > prefix.Length ? kv.Key[prefix.Length..] : "";
            if (string.IsNullOrEmpty(toolName))
                continue;
            if (c > bestCount || (c == bestCount && string.Compare(toolName, best, StringComparison.Ordinal) < 0))
            {
                bestCount = c;
                best = toolName;
            }
        }

        return best;
    }

    public static IReadOnlyList<(string Key, string Provider)> BuildConfiguredLlmModelCatalog(IConfiguration configuration)
    {
        var list = new List<(string Key, string Provider)>();
        var groqModel = configuration["Groq:Model"];
        if (string.IsNullOrWhiteSpace(groqModel))
            groqModel = "llama-3.3-70b-versatile";
        list.Add((SanitizeSegment($"groq/{groqModel.Trim()}"), "Groq"));

        var haiku = configuration["Anthropic:HaikuModel"];
        if (string.IsNullOrWhiteSpace(haiku))
            haiku = AgentModelSelector.DefaultHaikuModelId;
        var haikuKey = SanitizeSegment(haiku.Trim());
        list.Add((haikuKey, "Anthropic"));

        var sonnet = configuration["Anthropic:Model"];
        if (string.IsNullOrWhiteSpace(sonnet))
            sonnet = AgentModelSelector.DefaultSonnetModelId;
        var sonnetKey = SanitizeSegment(sonnet.Trim());
        if (!string.Equals(sonnetKey, haikuKey, StringComparison.OrdinalIgnoreCase))
            list.Add((sonnetKey, "Anthropic"));

        return list;
    }

    public static Dictionary<string, ModelUsage> MergeWithConfiguredLlmPlaceholders(
        Dictionary<string, ModelUsage> fromDb,
        IConfiguration configuration)
    {
        var catalog = BuildConfiguredLlmModelCatalog(configuration);
        var merged = new Dictionary<string, ModelUsage>(StringComparer.Ordinal);
        foreach (var (key, provider) in catalog)
        {
            if (fromDb.TryGetValue(key, out var existing))
                merged[key] = existing;
            else
            {
                merged[key] = new ModelUsage
                {
                    Model = key,
                    Provider = provider,
                    Messages = 0,
                    InputTokens = 0,
                    OutputTokens = 0,
                    CostUsd = 0,
                };
            }
        }

        foreach (var kv in fromDb)
        {
            if (!merged.ContainsKey(kv.Key))
                merged[kv.Key] = kv.Value;
        }

        return merged;
    }
}
