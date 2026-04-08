using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// TTS via OpenAI Audio API (tts-1 / tts-1-hd).
/// Detects language automatically from the text — no language-specific model needed.
/// Voice is selected per language for the most natural result.
/// </summary>
public class OpenAiSpeechService(HttpClient http, IConfiguration config) : ISpeechService
{
    private const string Endpoint = "https://api.openai.com/v1/audio/speech";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Best OpenAI voice per language for language-learning use.
    // "nova"    — warm, clear female voice (excellent for Spanish / French / Italian)
    // "shimmer" — soft female voice (good for East Asian languages)
    // "onyx"    — deep, calm male voice (Arabic / Russian)
    // "alloy"   — neutral, balanced voice (Germanic / Slavic)
    private static readonly Dictionary<string, string> LangVoiceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["es"] = "nova",
        ["fr"] = "nova",
        ["it"] = "nova",
        ["pt"] = "nova",
        ["de"] = "alloy",
        ["nl"] = "alloy",
        ["pl"] = "alloy",
        ["tr"] = "alloy",
        ["ar"] = "onyx",
        ["ru"] = "onyx",
        ["ja"] = "shimmer",
        ["zh"] = "shimmer",
        ["ko"] = "shimmer",
        ["en"] = "nova",
    };

    public async Task<SpeechResult> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = config["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "OpenAI API key is not configured. Set OPENAI_API_KEY as an environment variable.");

        var langBase = (request.LanguageCode ?? "en")
            .Split('-', '_')[0]
            .ToLowerInvariant();

        var voice = !string.IsNullOrWhiteSpace(request.VoiceId)
            ? request.VoiceId.Trim()
            : LangVoiceMap.GetValueOrDefault(langBase, "nova");

        // tts-1-hd for better quality; tts-1 for lower latency.
        var model = config["OpenAI:TtsModel"] ?? "tts-1-hd";

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model,
            input = request.Text,
            voice,
            response_format = "mp3",
            speed = 0.9   // slightly slower — better for language learning
        };

        req.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await http.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"OpenAI TTS request failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (audio.Length == 0)
            throw new InvalidOperationException("OpenAI TTS returned an empty audio payload.");

        return new SpeechResult(audio, "audio/mpeg");
    }
}
