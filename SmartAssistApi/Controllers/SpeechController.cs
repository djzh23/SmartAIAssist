using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpeechController(
    ISpeechService speechService,
    ClerkAuthService clerkAuthService,
    UsageService usageService,
    ILogger<SpeechController> logger) : ControllerBase
{
    [HttpPost("tts")]
    [EnableRateLimiting("agent_chat")]
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

    /// <summary>
    /// Public demo TTS endpoint — no Clerk auth required.
    /// Per-IP rate limit: 8 calls per day (stored in Redis under demo_tts:{ip}:{date}).
    /// Text capped at 500 characters. Used exclusively by the landing-page live demo.
    /// </summary>
    [HttpPost("demo-tts")]
    [EnableRateLimiting("agent_chat")]
    public async Task<IActionResult> DemoTextToSpeech([FromBody] SpeechRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text must not be empty." });

        if (request.Text.Length > 500)
            return BadRequest(new { error = $"Demo TTS text must not exceed 500 characters (received {request.Text.Length})." });

        if (string.IsNullOrWhiteSpace(request.LanguageCode))
            return BadRequest(new { error = "LanguageCode must not be empty." });

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var demoUserId = $"demo_tts:{ip}";
        const int demoLimit = 8;

        int currentUsage;
        try
        {
            currentUsage = await usageService.GetUsageTodayAsync(demoUserId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Demo TTS usage check failed for IP {IP}", ip);
            return StatusCode(503, new { error = "usage_check_failed" });
        }

        if (currentUsage >= demoLimit)
            return StatusCode(429, new { error = "demo_tts_limit_reached", message = "Audio demo limit reached. Sign up to continue." });

        logger.LogInformation("Demo TTS request. IP {IP} LangCode {LangCode} Usage {Usage}/{Limit}",
            ip, request.LanguageCode, currentUsage + 1, demoLimit);

        try
        {
            await usageService.IncrementUsageAsync(demoUserId);
            var speech = await speechService.SynthesizeAsync(request, cancellationToken);
            return File(speech.Audio, speech.ContentType);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Demo TTS configuration error");
            return StatusCode(500, new { error = "speech_config_error", message = "Speech service is not configured correctly." });
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Demo TTS provider request failed");
            return StatusCode(502, new { error = "speech_provider_error", message = "Speech provider returned an error." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo TTS endpoint failed");
            return StatusCode(500, new { error = "speech_error", message = "An internal error occurred." });
        }
    }
}
