using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SmartAssistApi.Services.Tools;

public static class TranslationTool
{
    private static readonly HttpClient _http = new();

    public static async Task<string> TranslateAsync(string text, string fromLang, string toLang)
    {
        try
        {
            var encoded = Uri.EscapeDataString(text);
            var url = $"https://api.mymemory.translated.net/get?q={encoded}&langpair={fromLang}|{toLang}";
            var response = await _http.GetFromJsonAsync<MyMemoryResponse>(url);
            var translated = response?.ResponseData?.TranslatedText;

            if (string.IsNullOrWhiteSpace(translated)
                || translated.Contains("INVALID", StringComparison.OrdinalIgnoreCase))
                return "[Translation unavailable]";

            return $"({translated})";
        }
        catch
        {
            return "[Translation unavailable]";
        }
    }

    private sealed record MyMemoryResponse(
        [property: JsonPropertyName("responseData")] MyMemoryData? ResponseData
    );

    private sealed record MyMemoryData(
        [property: JsonPropertyName("translatedText")] string? TranslatedText
    );
}
