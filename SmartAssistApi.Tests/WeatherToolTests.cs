using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class WeatherToolTests
{
    [Theory]
    [InlineData("berlin")]
    [InlineData("Berlin")]
    [InlineData("BERLIN")]
    public void GetWeather_KnownCity_ReturnsCityAndWeatherData(string city)
    {
        var result = WeatherTool.GetWeather(city);

        Assert.Contains("berlin", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("18°C", result);
    }

    [Fact]
    public void GetWeather_Hamburg_ReturnsCorrectWeather()
    {
        var result = WeatherTool.GetWeather("hamburg");

        Assert.Contains("14°C", result);
        Assert.Contains("regnerisch", result);
    }

    [Fact]
    public void GetWeather_München_ReturnsCorrectWeather()
    {
        var result = WeatherTool.GetWeather("münchen");

        Assert.Contains("22°C", result);
        Assert.Contains("sonnig", result);
    }

    [Fact]
    public void GetWeather_UnknownCity_ReturnsNotFoundMessage()
    {
        var result = WeatherTool.GetWeather("tokio");

        Assert.Contains("tokio", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("°C", result);
    }

    [Fact]
    public void GetWeather_EmptyString_ReturnsNotFoundMessage()
    {
        var result = WeatherTool.GetWeather("");

        Assert.Contains("verfügbar", result);
    }
}
