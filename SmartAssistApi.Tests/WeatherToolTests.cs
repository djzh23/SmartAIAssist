using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class WeatherToolTests
{
    [Theory]
    [InlineData(113, "☀️")]   // Clear / Sunny
    [InlineData(116, "⛅")]   // Partly cloudy
    [InlineData(119, "☁️")]   // Cloudy
    [InlineData(143, "🌫️")]   // Mist / Fog
    [InlineData(248, "🌫️")]   // Fog
    [InlineData(266, "🌦️")]   // Light drizzle
    [InlineData(296, "🌧️")]   // Light rain
    [InlineData(311, "🌨️")]   // Light freezing rain
    [InlineData(326, "❄️")]   // Light snow
    [InlineData(389, "⛈️")]   // Heavy rain with thunder
    [InlineData(999, "🌡️")]   // Unknown / default
    public void GetWeatherCondition_KnownCode_ReturnsCorrectEmoji(int code, string expectedEmoji)
    {
        var result = WeatherTool.GetWeatherCondition(code);

        Assert.Contains(expectedEmoji, result);
    }

    [Fact]
    public void GetWeatherCondition_UnknownCode_ReturnsDefault()
    {
        var result = WeatherTool.GetWeatherCondition(0);

        Assert.Contains("🌡️", result);
    }

    [Fact]
    public async Task GetWeatherAsync_InvalidCity_ReturnsNotFoundOrError()
    {
        var result = await WeatherTool.GetWeatherAsync("xyz_nonexistent_city_test_abc123");

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task GetWeatherAsync_EmptyCity_ReturnsPromptMessage()
    {
        var result = await WeatherTool.GetWeatherAsync("");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("Bitte", result, StringComparison.OrdinalIgnoreCase);
    }
}
