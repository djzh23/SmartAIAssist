using System.Text.Json;

namespace SmartAssistApi.Services.Tools;

public static class WeatherTool
{
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static async Task<string> GetWeatherAsync(string city)
    {
        try
        {
            // Step 1: Geocoding — city name → coordinates
            var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search" +
                         $"?name={Uri.EscapeDataString(city)}&count=1&language=de&format=json";
            var geoJson = await Http.GetStringAsync(geoUrl);
            var geo = JsonSerializer.Deserialize<GeoResponse>(geoJson, JsonOpts);

            if (geo?.Results is null || geo.Results.Length == 0)
                return $"Stadt '{city}' nicht gefunden. Bitte prüfe den Stadtnamen.";

            var loc = geo.Results[0];

            // Step 2: Weather from coordinates
            var weatherUrl = $"https://api.open-meteo.com/v1/forecast" +
                             $"?latitude={loc.Latitude}&longitude={loc.Longitude}" +
                             $"&current=temperature_2m,relative_humidity_2m," +
                             $"weather_code,wind_speed_10m,apparent_temperature" +
                             $"&timezone=auto";
            var weatherJson = await Http.GetStringAsync(weatherUrl);
            var weather = JsonSerializer.Deserialize<WeatherResponse>(weatherJson, JsonOpts);

            if (weather?.Current is null)
                return "Wetterdaten konnten nicht abgerufen werden.";

            var w = weather.Current;
            var condition = GetWeatherCondition(w.WeatherCode);

            return $"Wetter in {loc.Name} ({loc.Country}):\n" +
                   $"{condition}\n" +
                   $"🌡️ Temperatur: {w.Temperature2m}°C (gefühlt {w.ApparentTemperature}°C)\n" +
                   $"💨 Wind: {w.WindSpeed10m} km/h\n" +
                   $"💧 Luftfeuchtigkeit: {w.RelativeHumidity2m}%";
        }
        catch (Exception ex)
        {
            return $"Fehler beim Abrufen des Wetters für '{city}': {ex.Message}";
        }
    }

    public static string GetWeatherCondition(int code) => code switch
    {
        0          => "☀️ Klar und sonnig",
        1 or 2 or 3 => "⛅ Teilweise bewölkt",
        45 or 48   => "🌫️ Neblig",
        51 or 53 or 55 => "🌦️ Nieselregen",
        61 or 63 or 65 => "🌧️ Regen",
        71 or 73 or 75 => "❄️ Schneefall",
        80 or 81 or 82 => "🌧️ Regenschauer",
        95         => "⛈️ Gewitter",
        _          => "🌡️ Wechselhaft"
    };

    private record GeoResponse(GeoResult[]? Results);
    private record GeoResult(string Name, string Country, double Latitude, double Longitude);
    private record WeatherResponse(CurrentWeather? Current);
    private record CurrentWeather(
        double Temperature2m,
        double ApparentTemperature,
        double WindSpeed10m,
        int RelativeHumidity2m,
        int WeatherCode);
}
