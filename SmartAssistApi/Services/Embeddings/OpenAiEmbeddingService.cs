using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartAssistApi.Configuration;

namespace SmartAssistApi.Services.Embeddings;

public sealed class OpenAiEmbeddingService(
    HttpClient httpClient,
    IOptions<EmbeddingOptions> options,
    ILogger<OpenAiEmbeddingService> logger) : IEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly EmbeddingOptions _options = options.Value;

    public int Dimension => _options.Dimension;

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Embedding input text must not be empty.", nameof(text));

        var result = await EmbedInternalAsync([text], ct).ConfigureAwait(false);
        return result[0];
    }

    public Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return Task.FromResult(Array.Empty<float[]>());
        if (texts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Embedding batch contains empty text.", nameof(texts));

        return EmbedInternalAsync(texts, ct);
    }

    private async Task<float[][]> EmbedInternalAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (!string.Equals(_options.Provider, "openai", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Embedding provider '{_options.Provider}' is not supported yet.");
        if (string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
            throw new InvalidOperationException("Embeddings: OpenAI API key is missing.");

        using var req = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new OpenAiEmbeddingRequest(_options.Model, texts), JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var res = await httpClient.SendAsync(req, ct).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("OpenAI embedding request failed. Status {StatusCode}", (int)res.StatusCode);
            throw new InvalidOperationException($"OpenAI embeddings failed ({(int)res.StatusCode}): {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiEmbeddingResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("OpenAI embeddings response was empty.");

        var ordered = parsed.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();

        if (ordered.Length != texts.Count)
            throw new InvalidOperationException("OpenAI embeddings response count mismatch.");
        if (ordered.Any(e => e.Length != Dimension))
            throw new InvalidOperationException($"OpenAI embedding dimension mismatch. Expected {Dimension}.");

        return ordered;
    }

    private string BuildEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? "https://api.openai.com/v1/" : _options.BaseUrl;
        return new Uri(new Uri(baseUrl, UriKind.Absolute), "embeddings").ToString();
    }

    private sealed record OpenAiEmbeddingRequest(string Model, IReadOnlyList<string> Input);

    private sealed record OpenAiEmbeddingResponse(OpenAiEmbeddingItem[] Data);

    private sealed record OpenAiEmbeddingItem(int Index, float[] Embedding);
}
