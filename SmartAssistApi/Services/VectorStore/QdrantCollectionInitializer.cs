using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmartAssistApi.Configuration;
using SmartAssistApi.Services.Embeddings;

namespace SmartAssistApi.Services.VectorStore;

public sealed class QdrantCollectionInitializer(
    IHttpClientFactory httpClientFactory,
    IOptions<QdrantOptions> qdrantOptions,
    IEmbeddingService embeddings,
    ILogger<QdrantCollectionInitializer> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QdrantOptions _options = qdrantOptions.Value;

    public async Task EnsureCollectionAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Qdrant is disabled. Skipping collection initialization.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.CollectionName))
            throw new InvalidOperationException("Qdrant collection name must not be empty.");

        var client = httpClientFactory.CreateClient("qdrant");
        using var getReq = BuildRequest(HttpMethod.Get, $"/collections/{_options.CollectionName}");
        using var getRes = await client.SendAsync(getReq, ct).ConfigureAwait(false);
        if (getRes.IsSuccessStatusCode)
        {
            logger.LogInformation("Qdrant collection {Collection} already exists.", _options.CollectionName);
            return;
        }

        if (getRes.StatusCode != HttpStatusCode.NotFound)
        {
            var getBody = await getRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Failed to check Qdrant collection '{_options.CollectionName}' ({(int)getRes.StatusCode}): {getBody}");
        }

        var payload = new
        {
            vectors = new
            {
                size = embeddings.Dimension,
                distance = "Cosine",
            },
        };

        using var putReq = BuildRequest(HttpMethod.Put, $"/collections/{_options.CollectionName}");
        putReq.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var putRes = await client.SendAsync(putReq, ct).ConfigureAwait(false);
        if (!putRes.IsSuccessStatusCode)
        {
            var putBody = await putRes.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Failed to create Qdrant collection '{_options.CollectionName}' ({(int)putRes.StatusCode}): {putBody}");
        }

        logger.LogInformation(
            "Qdrant collection {Collection} created with dimension {Dimension}.",
            _options.CollectionName,
            embeddings.Dimension);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            req.Headers.Add("api-key", _options.ApiKey);
        return req;
    }
}
