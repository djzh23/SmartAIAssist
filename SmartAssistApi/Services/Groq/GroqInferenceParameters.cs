using SmartAssistApi.Configuration;

namespace SmartAssistApi.Services.Groq;

/// <summary>Sampling- und Längen-Parameter pro Tool (Groq + konsistent für Anthropic-Temperature).</summary>
public static class GroqInferenceParameters
{
    public static int MaxTokensFor(string? toolType)
    {
        var skill = SkillRegistry.FindSkill(toolType);
        if (skill is not null)
            return int.Clamp(skill.MaxTokens, 50, 8000);

        var t = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant();
        return t switch
        {
            "general" => 600,
            "language" => 400,
            "jobanalyzer" => 800,
            "interviewprep" => 880,
            "programming" => 1000,
            _ => 600,
        };
    }

    /// <summary>0.2–0.6 je nach Tool; nie über 0.7.</summary>
    public static double TemperatureFor(string? toolType)
    {
        var skill = SkillRegistry.FindSkill(toolType);
        if (skill is not null)
            return Math.Min(0.7, skill.Temperature);

        var t = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant();
        return t switch
        {
            "jobanalyzer" => 0.18,
            "interviewprep" => 0.28,
            "programming" => 0.2,
            "language" => 0.6,
            _ => 0.5,
        };
    }

    public static GroqSamplingOptions SamplingFor(string? toolType) =>
        new(
            Temperature: TemperatureFor(toolType),
            FrequencyPenalty: 0.3,
            PresencePenalty: 0.1);

    /// <summary>Anthropic SDK erwartet decimal; deckelt auf 0.7.</summary>
    public static decimal AnthropicTemperatureFor(string? toolType) =>
        (decimal)Math.Min(0.7, TemperatureFor(toolType));
}

public sealed record GroqSamplingOptions(
    double? Temperature,
    double FrequencyPenalty,
    double PresencePenalty);
