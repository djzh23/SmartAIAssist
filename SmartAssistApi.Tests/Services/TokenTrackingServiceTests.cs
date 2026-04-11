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
    [InlineData("groq/llama-3.3-70b-versatile", 1_000_000, 1_000_000, 0)]
    public void CalculateCost_ReturnsCorrectAmount(string model, int input, int output, decimal expected)
    {
        var result = TokenTrackingService.CalculateCost(model, input, output);
        Assert.Equal(expected, result, precision: 4);
    }

    /// <summary>Haiku $1/MTok input: 1M cache read at 0.10× → $0.10, no other usage.</summary>
    [Fact]
    public void CalculateCost_CacheReadOnly_Haiku_AppliesPointOneMultiplier()
    {
        var cost = TokenTrackingService.CalculateCost("claude-haiku-4-5-20251001", 0, 0, 0, 1_000_000);
        Assert.Equal(0.10m, cost, precision: 4);
    }

    /// <summary>Haiku $1/MTok input: 1M cache creation at 1.25× → $1.25.</summary>
    [Fact]
    public void CalculateCost_CacheCreationOnly_Haiku_AppliesOnePointTwoFiveMultiplier()
    {
        var cost = TokenTrackingService.CalculateCost("claude-haiku-4-5-20251001", 0, 0, 1_000_000, 0);
        Assert.Equal(1.25m, cost, precision: 4);
    }

    [Fact]
    public void CalculateCost_NegativeCacheCreation_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TokenTrackingService.CalculateCost("claude-haiku-4-5-20251001", 100, 0, -1, 0));
    }

    [Fact]
    public void ParseTopToolFromTcFields_Empty_ReturnsNull()
    {
        Assert.Null(TokenTrackingService.ParseTopToolFromTcFields(new Dictionary<string, string>()));
    }

    [Fact]
    public void ParseTopToolFromTcFields_SingleTool_ReturnsTool()
    {
        var map = new Dictionary<string, string> { ["tc_general"] = "5" };
        Assert.Equal("general", TokenTrackingService.ParseTopToolFromTcFields(map));
    }

    [Fact]
    public void ParseTopToolFromTcFields_PicksHighestCount()
    {
        var map = new Dictionary<string, string>
        {
            ["tc_general"] = "3",
            ["tc_language"] = "10",
            ["messages"] = "13",
        };
        Assert.Equal("language", TokenTrackingService.ParseTopToolFromTcFields(map));
    }

    [Fact]
    public void ParseTopToolFromTcFields_TieBreaksLexicographically()
    {
        var map = new Dictionary<string, string>
        {
            ["tc_zebra"] = "5",
            ["tc_apple"] = "5",
        };
        Assert.Equal("apple", TokenTrackingService.ParseTopToolFromTcFields(map));
    }
}
