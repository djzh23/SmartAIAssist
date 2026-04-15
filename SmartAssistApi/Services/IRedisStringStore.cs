namespace SmartAssistApi.Services;

/// <summary>Minimal string get/set for Upstash-backed features (sessions, applications).</summary>
public interface IRedisStringStore
{
    Task<string?> StringGetAsync(string key, CancellationToken cancellationToken = default);

    Task StringSetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task StringDeleteAsync(string key, CancellationToken cancellationToken = default);
}
