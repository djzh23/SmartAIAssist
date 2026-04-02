namespace SmartAssistApi.Services.Tools;

public static class WeatherTool
{
    public static string GetWeather(string city)
    {
        // Simuliert eine Wetter-API (später kannst du echte API einbauen)
        var weathers = new Dictionary<string, string>
        {
            ["berlin"] = "18°C, bewölkt",
            ["hamburg"] = "14°C, regnerisch",
            ["münchen"] = "22°C, sonnig"
        };

        var key = city.ToLower();
        return weathers.TryGetValue(key, out var result)
            ? $"Wetter in {city}: {result}"
            : $"Keine Daten für {city} verfügbar.";
    }
}
