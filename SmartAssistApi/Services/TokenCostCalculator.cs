namespace SmartAssistApi.Services;

public static class TokenCostCalculator
{
    /// <summary>
    /// Estimate per-turn LLM cost in USD from token counts and model id.
    /// Values are per 1M tokens and can be updated when pricing changes.
    /// </summary>
    public static decimal Estimate(
        int inputTokens,
        int outputTokens,
        int cacheCreationTokens,
        int cacheReadTokens,
        string? model)
    {
        var (inputPrice, outputPrice, cacheWritePrice, cacheReadPrice) = model switch
        {
            var m when m?.Contains("claude-3-5-sonnet", StringComparison.OrdinalIgnoreCase) == true
                => (3.0m, 15.0m, 3.75m, 0.30m),
            var m when m?.Contains("claude-3-haiku", StringComparison.OrdinalIgnoreCase) == true
                => (0.25m, 1.25m, 0.30m, 0.03m),
            var m when m?.Contains("claude-sonnet-4", StringComparison.OrdinalIgnoreCase) == true
                => (3.0m, 15.0m, 3.75m, 0.30m),
            var m when m?.Contains("claude-haiku-4-5", StringComparison.OrdinalIgnoreCase) == true
                => (1.0m, 5.0m, 1.25m, 0.10m),
            var m when m?.Contains("groq", StringComparison.OrdinalIgnoreCase) == true
                => (0.0m, 0.0m, 0.0m, 0.0m),
            _ => (3.0m, 15.0m, 3.75m, 0.30m),
        };

        var nonCachedInput = Math.Max(0, inputTokens - cacheReadTokens);
        var cost = (nonCachedInput * inputPrice / 1_000_000m)
                 + (Math.Max(0, outputTokens) * outputPrice / 1_000_000m)
                 + (Math.Max(0, cacheCreationTokens) * cacheWritePrice / 1_000_000m)
                 + (Math.Max(0, cacheReadTokens) * cacheReadPrice / 1_000_000m);

        return Math.Max(0, cost);
    }
}
