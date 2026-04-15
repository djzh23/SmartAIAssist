using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Services;

/// <summary>Thin Upstash REST string store (same host/token as LearningMemoryService).</summary>
public class UpstashRedisStringStore(
    IConfiguration config,
    HttpClient http) : IRedisStringStore
{
    private readonly string _restUrl = RequireUrl(config["Upstash:RestUrl"]);
    private readonly string _restToken = RequireToken(config["Upstash:RestToken"]);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string RequireUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Upstash:RestUrl is missing.");
        return url.Trim().TrimEnd('/');
    }

    private static string RequireToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Upstash:RestToken is missing.");
        return token.Trim();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
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
