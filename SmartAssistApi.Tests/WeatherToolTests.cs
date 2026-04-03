using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class WeatherToolTests
{
    [Theory]
    [InlineData(0, "☀️")]
    [InlineData(1, "⛅")]
    [InlineData(2, "⛅")]
    [InlineData(3, "⛅")]
    [InlineData(45, "🌫️")]
    [InlineData(51, "🌦️")]
    [InlineData(61, "🌧️")]
    [InlineData(71, "❄️")]
    [InlineData(80, "🌧️")]
    [InlineData(95, "⛈️")]
    [InlineData(99, "🌡️")]
    public void GetWeatherCondition_KnownCode_ReturnsCorrectEmoji(int code, string expectedEmoji)
    {
        var result = WeatherTool.GetWeatherCondition(code);

        Assert.Contains(expectedEmoji, result);
    }

    [Fact]
    public void GetWeatherCondition_UnknownCode_ReturnsDefault()
    {
        var result = WeatherTool.GetWeatherCondition(999);

        Assert.Contains("🌡️", result);
    }

    [Fact]
    public async Task GetWeatherAsync_InvalidCity_ReturnsNotFoundMessage()
    {
        var result = await WeatherTool.GetWeatherAsync("xyz_nonexistent_city_test_abc123");

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("nicht gefunden", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetWeatherAsync_EmptyCity_ReturnsErrorOrNotFound()
    {
        var result = await WeatherTool.GetWeatherAsync("");

        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
