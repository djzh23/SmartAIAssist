using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Services;

/// <summary>Upstash REST string GET/SET/DELETE/EXPIRE (same pattern as LearningMemoryService).</summary>
public sealed class UpstashRedisStringStore(IConfiguration config, HttpClient http) : IRedisStringStore
{
    private readonly string _restUrl = config["Upstash:RestUrl"]?.Trim().TrimEnd('/')
        ?? throw new InvalidOperationException("Upstash:RestUrl missing");
    private readonly string _restToken = config["Upstash:RestToken"]?.Trim()
        ?? throw new InvalidOperationException("Upstash:RestToken missing");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var relative = path.StartsWith('/') ? path : "/" + path;
        var combined = $"{_restUrl}{relative}";
        var req = new HttpRequestMessage(method, combined);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        return req;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Upstash '{operation}' failed {(int)response.StatusCode}: {body}");
        return body;
    }

    private static UpstashEnvelope? DeserializeUpstash(string body, string operation)
    {
        var data = JsonSerializer.Deserialize<UpstashEnvelope>(body, JsonOpts)
            ?? throw new InvalidOperationException($"Upstash '{operation}' empty payload.");
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException($"Upstash '{operation}': {data.Error}");
        return data;
    }

    private static string? FormatResultAsString(object? result)
    {
        if (result is null) return null;
        if (result is JsonElement el)
        {
            if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            return el.ToString();
        }

        return result.ToString();
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/get/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"get:{key}", cancellationToken).ConfigureAwait(false);
        var data = DeserializeUpstash(body, $"get:{key}");
        return FormatResultAsString(data?.Result);
    }

    public async Task SetAsync(string key, string value, int? ttlSeconds, CancellationToken cancellationToken = default)
    {
        var path = $"/set/{Uri.EscapeDataString(key)}";
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = new StringContent(value, Encoding.UTF8, "text/plain");
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
        _ = await SendAsync(req, $"set-body:{key}", cancellationToken).ConfigureAwait(false);
        if (ttlSeconds is > 0)
        {
            using var ex = CreateRequest(HttpMethod.Post, $"/expire/{Uri.EscapeDataString(key)}/{ttlSeconds.Value}");
            _ = await SendAsync(ex, $"expire:{key}", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        using var req = CreateRequest(HttpMethod.Post, $"/del/{Uri.EscapeDataString(key)}");
        _ = await SendAsync(req, $"del:{key}", cancellationToken).ConfigureAwait(false);
    }

    private sealed record UpstashEnvelope(object? Result, string? Error);
}
