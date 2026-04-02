namespace SmartAssistApi.Models;

public record SpeechRequest(
    string Text,
    string LanguageCode,
    string? VoiceId = null,
    string? ModelId = null
);

public record SpeechResult(byte[] Audio, string ContentType);
