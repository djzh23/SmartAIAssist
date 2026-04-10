using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class TokenTrackingServiceTests
{
    [Theory]
    [InlineData("claude-haiku-4-5-20251001", 1000, 500, 0.0035)]
    [InlineData("claude-sonnet-4-20250514", 1000, 500, 0.0105)]
    [InlineData("claude-haiku-4-5-20251001", 0, 0, 0)]
    [InlineData("claude-haiku-4-5-20251001", 1_000_000, 1_000_000, 6.00)]
    [InlineData("claude-sonnet-4-20250514", 1_000_000, 1_000_000, 18.00)]
    [InlineData("unknown-model", 1000, 500, 0.0105)]
    public void CalculateCost_ReturnsCorrectAmount(string model, int input, int output, decimal expected)
    {
        var result = TokenTrackingService.CalculateCost(model, input, output);
        Assert.Equal(expected, decimal.Round(result, 4));
    }
}
