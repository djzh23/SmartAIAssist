namespace SmartAssistApi.Services;

/// <summary>Minimal string key/value over Upstash Redis REST (or in-memory for tests).</summary>
public interface IRedisStringStore
{
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, int? ttlSeconds, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
