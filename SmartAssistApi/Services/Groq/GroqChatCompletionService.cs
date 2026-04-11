using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services.Groq;

public sealed class GroqOptions
{
    public const string SectionName = "Groq";

    /// <summary>Also read from env GROQ_API_KEY (mapped in Program.cs).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Also read from env GROQ_MODEL (mapped in Program.cs).</summary>
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    /// <summary>When true and <see cref="ApiKey"/> is set, agent tries Groq before Anthropic for eligible requests.</summary>
    public bool UseAsPrimary { get; set; } = true;

    public double Temperature { get; set; } = 0.7;
}

/// <summary>Groq OpenAI-compatible chat completions (primary LLM when configured).</summary>
public sealed class GroqChatCompletionService
{
    private readonly HttpClient _http;
    private readonly GroqOptions _opt;
    private readonly ILogger<GroqChatCompletionService> _logger;

    public GroqChatCompletionService(
        HttpClient http,
        IOptions<GroqOptions> options,
        ILogger<GroqChatCompletionService> logger)
    {
        _http = http;
        _opt = options.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_opt.ApiKey);

    public async Task<GroqCompletionResult> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<GroqChatMessage> messages,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new GroqCompletionResult
            {
                Success = false,
                Error = "Groq API key is not configured.",
            };
        }

        var model = string.IsNullOrWhiteSpace(_opt.Model) ? "llama-3.3-70b-versatile" : _opt.Model.Trim();

        var payloadMessages = new List<Dictionary<string, object>>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            payloadMessages.Add(new Dictionary<string, object> { ["role"] = "system", ["content"] = systemPrompt });

        foreach (var m in messages)
            payloadMessages.Add(new Dictionary<string, object> { ["role"] = m.Role, ["content"] = m.Content });

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = payloadMessages,
            ["max_tokens"] = maxTokens,
            ["temperature"] = _opt.Temperature,
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opt.ApiKey.Trim());

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq API error {Status}: {Body}", (int)resp.StatusCode, raw.Length > 500 ? raw[..500] : raw);
                return new GroqCompletionResult
                {
                    Success = false,
                    Model = model,
                    Error = $"Groq HTTP {(int)resp.StatusCode}: {raw}",
                };
            }

            var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";

            var inTok = 0;
            var outTok = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) inTok = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct)) outTok = ct.GetInt32();
            }

            return new GroqCompletionResult
            {
                Success = !string.IsNullOrWhiteSpace(content),
                Content = content,
                Model = model,
                InputTokens = inTok,
                OutputTokens = outTok,
                Error = string.IsNullOrWhiteSpace(content) ? "Groq returned empty content." : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Groq request failed");
            return new GroqCompletionResult
            {
                Success = false,
                Model = model,
                Error = $"Groq exception: {ex.Message}",
            };
        }
    }
}
