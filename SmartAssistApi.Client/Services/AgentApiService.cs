using System.Net.Http.Json;
using System.Text.Json;
using SmartAssistApi.Client.Models;

namespace SmartAssistApi.Client.Services;

public class AgentApiService(HttpClient http)
{
    public async Task<AgentResponse> AskAsync(
        string message,
        string? sessionId = null,
        bool languageLearningMode = false,
        string? targetLanguage = null,
        string? nativeLanguage = null,
        string? targetLanguageCode = null,
        string? nativeLanguageCode = null,
        string? level = null,
        string? learningGoal = null)
    {
        var request = new AgentRequest(
            message, sessionId, languageLearningMode,
            targetLanguage, nativeLanguage, targetLanguageCode, nativeLanguageCode, level, learningGoal);

        var response = await http.PostAsJsonAsync("api/agent/ask", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentResponse>()
            ?? throw new InvalidOperationException("Empty response from agent.");
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await http.DeleteAsync($"api/agent/session/{sessionId}");
    }

    public async Task<byte[]?> GenerateSpeechAsync(
        string text,
        string languageCode,
        string? voiceId = null,
        string? modelId = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            Text = text,
            LanguageCode = languageCode,
            VoiceId = voiceId,
            ModelId = modelId
        };

        var response = await http.PostAsJsonAsync("api/speech/tts", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var details = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Speech request failed ({(int)response.StatusCode}): {ExtractErrorMessage(details)}",
                null,
                response.StatusCode);
        }

        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return audio.Length > 0 ? audio : null;
    }

    private static string ExtractErrorMessage(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return "Unknown speech provider error.";

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var errorProp) &&
                errorProp.ValueKind == JsonValueKind.String)
            {
                var message = errorProp.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                    return message;
            }
        }
        catch (JsonException)
        {
            // Non-JSON payload.
        }

        return payload.Trim();
    }
}
