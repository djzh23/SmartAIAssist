using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAssistApi.Services.Tools;

public static class WeatherTool
{
    private static readonly HttpClient Http;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    static WeatherTool()
    {
        Http = new HttpClient();
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("SmartAssistApi/1.0 (weather assistant)");
    }

    public static async Task<string> GetWeatherAsync(string city)
    {
        if (string.IsNullOrWhiteSpace(city))
            return "Bitte gib einen Stadtnamen ein.";

        try
        {
            var url = $"https://wttr.in/{Uri.EscapeDataString(city)}?format=j1";
            var json = await Http.GetStringAsync(url);
            var weather = JsonSerializer.Deserialize<WttrResponse>(json, JsonOpts);

            if (weather?.CurrentCondition is null || weather.CurrentCondition.Length == 0)
                return $"Stadt '{city}' nicht gefunden. Bitte prüfe den Stadtnamen.";

            var c = weather.CurrentCondition[0];
            var area = weather.NearestArea?[0];
            var cityName = area?.AreaName?[0]?.Value ?? city;
            var country = area?.Country?[0]?.Value ?? "";
            var desc = c.WeatherDesc?[0]?.Value ?? "";
            var emoji = GetWeatherCondition(int.TryParse(c.WeatherCode, out var code) ? code : 0);

            return $"Wetter in {cityName} ({country}):\n" +
                   $"{emoji} {desc}\n" +
                   $"🌡️ Temperatur: {c.TempC}°C (gefühlt {c.FeelsLikeC}°C)\n" +
                   $"💨 Wind: {c.WindspeedKmph} km/h\n" +
                   $"💧 Luftfeuchtigkeit: {c.Humidity}%";
        }
        catch (HttpRequestException)
        {
            return $"Stadt '{city}' nicht gefunden oder keine Verbindung möglich.";
        }
        catch (Exception ex)
        {
            return $"Fehler beim Abrufen des Wetters für '{city}': {ex.Message}";
        }
    }

    // wttr.in weather codes (different from WMO codes used by Open-Meteo)
    public static string GetWeatherCondition(int code) => code switch
    {
        113                                                => "☀️ Klar und sonnig",
        116                                                => "⛅ Teilweise bewölkt",
        119 or 122                                         => "☁️ Bewölkt",
        143 or 248 or 260                                  => "🌫️ Neblig",
        176 or 263 or 266 or 293 or 296                    => "🌦️ Leichter Regen",
        299 or 302 or 305 or 308 or 353 or 356 or 359      => "🌧️ Regen",
        185 or 281 or 284 or 311 or 314 or 317 or 320      => "🌨️ Schneeregen",
        179 or 227 or 230 or 323 or 326 or 329 or 332
            or 335 or 338 or 368 or 371 or 374 or 377      => "❄️ Schneefall",
        200 or 386 or 389 or 392 or 395                    => "⛈️ Gewitter",
        _                                                  => "🌡️ Wechselhaft"
    };

    private record WttrResponse(
        [property: JsonPropertyName("current_condition")] WttrCondition[]? CurrentCondition,
        [property: JsonPropertyName("nearest_area")] WttrArea[]? NearestArea
    );

    private record WttrCondition(
        [property: JsonPropertyName("temp_C")] string TempC,
        [property: JsonPropertyName("FeelsLikeC")] string FeelsLikeC,
        [property: JsonPropertyName("windspeedKmph")] string WindspeedKmph,
        [property: JsonPropertyName("humidity")] string Humidity,
        [property: JsonPropertyName("weatherCode")] string WeatherCode,
        [property: JsonPropertyName("weatherDesc")] WttrValue[]? WeatherDesc
    );

    private record WttrArea(
        [property: JsonPropertyName("areaName")] WttrValue[]? AreaName,
        [property: JsonPropertyName("country")] WttrValue[]? Country
    );

    private record WttrValue(
        [property: JsonPropertyName("value")] string Value
    );
}
