using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public class ElevenLabsSpeechService(HttpClient http, IConfiguration config) : ISpeechService
{
    private const string BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SpeechResult> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Text must not be empty.", nameof(request));

        var languageCode = request.LanguageCode?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(languageCode))
            throw new ArgumentException("LanguageCode must not be empty.", nameof(request));
        var baseLanguageCode = GetBaseLanguageCode(languageCode);

        var apiKey = config["ELEVENLABS_API_KEY"] ?? config["ElevenLabs:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ElevenLabs API key is not configured.");

        var voiceId = ResolveVoiceId(request.VoiceId, languageCode, baseLanguageCode);
        var modelId = string.IsNullOrWhiteSpace(request.ModelId)
            ? (config["ElevenLabs:ModelId"] ?? "eleven_multilingual_v2")
            : request.ModelId.Trim();
        var outputFormat = config["ElevenLabs:OutputFormat"] ?? "mp3_44100_128";
        var endpoint = $"{BaseUrl}{voiceId}?output_format={Uri.EscapeDataString(outputFormat)}";

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/mpeg"));
        httpRequest.Headers.TryAddWithoutValidation("xi-api-key", apiKey);

        var payload = new
        {
            text = request.Text,
            model_id = modelId,
            voice_settings = new
            {
                stability = 0.4,
                similarity_boost = 0.7
            }
        };

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"ElevenLabs request failed ({(int)response.StatusCode}): {errorBody}",
                null,
                response.StatusCode);
        }

        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (audio.Length == 0)
            throw new InvalidOperationException("ElevenLabs returned an empty audio payload.");

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "audio/mpeg";
        return new SpeechResult(audio, contentType);
    }

    private string ResolveVoiceId(string? requestVoiceId, string languageCode, string baseLanguageCode)
    {
        if (!string.IsNullOrWhiteSpace(requestVoiceId))
            return requestVoiceId.Trim();

        // Lookup order:
        // 1) ELEVENLABS_VOICE_<LANG> (env var, e.g. ELEVENLABS_VOICE_ES)
        // 2) ElevenLabs:VoiceIds:{lang}
        // 3) ElevenLabs:VoiceIds:{baseLang}
        // 4) ELEVENLABS_VOICE_DEFAULT
        // 5) ElevenLabs:VoiceIds:default
        // 6) ElevenLabs:DefaultVoiceId
        var envSpecific = Environment.GetEnvironmentVariable($"ELEVENLABS_VOICE_{ToEnvKey(languageCode)}")
                       ?? Environment.GetEnvironmentVariable($"ELEVENLABS_VOICE_{ToEnvKey(baseLanguageCode)}");
        if (!string.IsNullOrWhiteSpace(envSpecific))
            return envSpecific.Trim();

        var languageSpecific = config[$"ElevenLabs:VoiceIds:{languageCode}"];
        if (!string.IsNullOrWhiteSpace(languageSpecific))
            return languageSpecific.Trim();

        var baseLanguageSpecific = config[$"ElevenLabs:VoiceIds:{baseLanguageCode}"];
        if (!string.IsNullOrWhiteSpace(baseLanguageSpecific))
            return baseLanguageSpecific.Trim();

        var defaultEnv = Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_DEFAULT");
        if (!string.IsNullOrWhiteSpace(defaultEnv))
            return defaultEnv.Trim();

        var defaultMapped = config["ElevenLabs:VoiceIds:default"];
        if (!string.IsNullOrWhiteSpace(defaultMapped))
            return defaultMapped.Trim();

        var defaultVoice = config["ElevenLabs:DefaultVoiceId"];
        if (!string.IsNullOrWhiteSpace(defaultVoice))
            return defaultVoice.Trim();

        throw new InvalidOperationException(
            $"No ElevenLabs voice ID configured for language '{languageCode}'. " +
            $"Set ELEVENLABS_VOICE_{ToEnvKey(baseLanguageCode)}, ElevenLabs:VoiceIds:<code> or ElevenLabs:DefaultVoiceId.");
    }

    private static string GetBaseLanguageCode(string languageCode)
    {
        var index = languageCode.IndexOfAny(['-', '_']);
        return index > 0 ? languageCode[..index] : languageCode;
    }

    private static string ToEnvKey(string languageCode)
    {
        return languageCode.Replace('-', '_').Replace('.', '_').ToUpperInvariant();
    }
}
