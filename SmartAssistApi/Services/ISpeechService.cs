using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public interface ISpeechService
{
    Task<SpeechResult> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default);
}
