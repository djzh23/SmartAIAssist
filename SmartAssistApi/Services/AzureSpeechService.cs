using System.Net.Http.Headers;
using System.Text;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// TTS via Microsoft Azure Cognitive Services Speech (Neural voices).
/// Free tier (F0): 500,000 characters/month — no credit card required.
/// Voices are hand-picked per language for natural-sounding language learning audio.
/// </summary>
public class AzureSpeechService(HttpClient http, IConfiguration config) : ISpeechService
{
    // Best Azure Neural voice per language code.
    // Female voices are generally preferred for language learning (clearer articulation).
    private static readonly Dictionary<string, (string Voice, string Locale)> VoiceMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["es"] = ("es-ES-ElviraNeural",    "es-ES"),   // Spanish (Spain) — warm, clear
            ["fr"] = ("fr-FR-DeniseNeural",    "fr-FR"),   // French
            ["de"] = ("de-DE-KatjaNeural",     "de-DE"),   // German
            ["it"] = ("it-IT-ElsaNeural",      "it-IT"),   // Italian
            ["pt"] = ("pt-BR-FranciscaNeural", "pt-BR"),   // Portuguese (Brazil)
            ["ar"] = ("ar-SA-ZariyahNeural",   "ar-SA"),   // Arabic
            ["ja"] = ("ja-JP-NanamiNeural",    "ja-JP"),   // Japanese
            ["zh"] = ("zh-CN-XiaoxiaoNeural",  "zh-CN"),   // Chinese Mandarin
            ["ko"] = ("ko-KR-SunHiNeural",     "ko-KR"),   // Korean
            ["ru"] = ("ru-RU-SvetlanaNeural",  "ru-RU"),   // Russian
            ["nl"] = ("nl-NL-ColetteNeural",   "nl-NL"),   // Dutch
            ["pl"] = ("pl-PL-ZofiaNeural",     "pl-PL"),   // Polish
            ["tr"] = ("tr-TR-EmelNeural",      "tr-TR"),   // Turkish
            ["en"] = ("en-US-JennyNeural",     "en-US"),   // English
        };

    public async Task<SpeechResult> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = config["AZURE_SPEECH_KEY"] ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Azure Speech API key is not configured. Set AZURE_SPEECH_KEY as an environment variable.");

        var region = config["AZURE_SPEECH_REGION"] ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION") ?? "westeurope";
        var endpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";

        var langBase = (request.LanguageCode ?? "en")
            .Split('-', '_')[0]
            .ToLowerInvariant();

        var (voice, locale) = !string.IsNullOrWhiteSpace(request.VoiceId)
            ? (request.VoiceId.Trim(), request.LanguageCode ?? "en-US")
            : VoiceMap.GetValueOrDefault(langBase, ("en-US-JennyNeural", "en-US"));

        // SSML with rate adjustment for language learning
        var ssml = $"""
            <speak version='1.0' xml:lang='{locale}'>
              <voice name='{voice}'>
                <prosody rate='-10%'>{EscapeXml(request.Text)}</prosody>
              </voice>
            </speak>
            """;

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
        req.Headers.Add("X-Microsoft-OutputFormat", "audio-24khz-48kbitrate-mono-mp3");
        req.Headers.UserAgent.ParseAdd("SmartAssist/1.0");
        req.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");

        using var response = await http.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Azure Speech TTS request failed ({(int)response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (audio.Length == 0)
            throw new InvalidOperationException("Azure Speech returned an empty audio payload.");

        return new SpeechResult(audio, "audio/mpeg");
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
