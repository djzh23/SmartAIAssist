using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpeechController(
    ISpeechService speechService,
    ClerkAuthService clerkAuthService,
    ILogger<SpeechController> logger) : ControllerBase
{
    [HttpPost("tts")]
    public async Task<IActionResult> TextToSpeech([FromBody] SpeechRequest request, CancellationToken cancellationToken)
    {
        var (_, isAnonymous) = clerkAuthService.ExtractUserId(Request);
        if (isAnonymous)
            return Unauthorized(new { error = "auth_required", message = "You must be signed in to use audio." });

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text must not be empty." });

        if (request.Text.Length > 1200)
            return BadRequest(new { error = $"Text must not exceed 1200 characters (received {request.Text.Length})." });

        if (string.IsNullOrWhiteSpace(request.LanguageCode))
            return BadRequest(new { error = "LanguageCode must not be empty." });

        try
        {
            var speech = await speechService.SynthesizeAsync(request, cancellationToken);
            return File(speech.Audio, speech.ContentType);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Speech configuration error");
            return StatusCode(500, new { error = "speech_config_error", message = "Speech service is not configured correctly." });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Speech provider request failed");
            return StatusCode(502, new { error = "speech_provider_error", message = "Speech provider returned an error." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Speech endpoint failed");
            return StatusCode(500, new { error = "speech_error", message = "An internal error occurred." });
        }
    }
}
