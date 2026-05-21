using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Services;

/// <summary>Thin Upstash REST string store (same host/token as LearningMemoryRedisService).</summary>
public class UpstashRedisStringStore(
    IConfiguration config,
    HttpClient http) : IRedisStringStore
{
    private readonly string? _restUrl = NormalizeUrl(config["Upstash:RestUrl"]);
    private readonly string? _restToken = NormalizeToken(config["Upstash:RestToken"]);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return url.Trim().TrimEnd('/');
    }

    private static string? NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return token.Trim();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_restUrl) || string.IsNullOrWhiteSpace(_restToken))
            throw new InvalidOperationException("Upstash Redis is not configured. Set Upstash:RestUrl and Upstash:RestToken.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        EnsureConfigured();
        var relative = path.StartsWith('/') ? path : "/" + path;
        var combined = $"{_restUrl}{relative}";
        var req = new HttpRequestMessage(method, combined);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        return req;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string operation)
    {
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Upstash '{operation}' failed {(int)response.StatusCode}: {body}");
        return body;
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

    private static UpstashResult? DeserializeUpstash(string body, string operation)
    {
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException($"Upstash '{operation}' empty payload.");
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException($"Upstash '{operation}': {data.Error}");
        return data;
    }

    public async Task<string?> StringGetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var req = CreateRequest(HttpMethod.Get, $"/get/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"get:{key}");
        var data = DeserializeUpstash(body, $"get:{key}");
        return FormatResultAsString(data?.Result);
    }

    public async Task<IReadOnlyList<string?>> StringGetManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (keys.Count == 0)
            return Array.Empty<string?>();
        if (keys.Count == 1)
            return [await StringGetAsync(keys[0], cancellationToken).ConfigureAwait(false)];

        var commands = new object[keys.Count][];
        for (var i = 0; i < keys.Count; i++)
            commands[i] = ["GET", keys[i]];

        using var req = CreateRequest(HttpMethod.Post, "/pipeline");
        req.Content = new StringContent(JsonSerializer.Serialize(commands, JsonOpts), Encoding.UTF8, "application/json");
        var body = await SendAsync(req, "pipeline:get-many").ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Upstash pipeline:get-many expected array response.");

        var results = new string?[keys.Count];
        var idx = 0;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                var msg = err.GetString();
                if (!string.IsNullOrEmpty(msg))
                    throw new InvalidOperationException($"Redis pipeline error: {msg}");
            }

            object? result = null;
            if (el.TryGetProperty("result", out var resEl))
                result = resEl;
            results[idx++] = FormatResultAsString(result);
        }

        return results;
    }

    public async Task StringSetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = $"/set/{Uri.EscapeDataString(key)}";
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = new StringContent(value, Encoding.UTF8, "text/plain");
        _ = await SendAsync(req, $"set-body:{key}");
    }

    public async Task StringDeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var req = CreateRequest(HttpMethod.Get, $"/del/{Uri.EscapeDataString(key)}");
        _ = await SendAsync(req, $"del:{key}");
    }

    private sealed record UpstashResult(
        [property: JsonPropertyName("result")] object? Result,
        [property: JsonPropertyName("error")] string? Error);
}
