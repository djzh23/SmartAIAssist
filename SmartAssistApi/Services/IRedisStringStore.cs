namespace SmartAssistApi.Services;

/// <summary>Minimal string get/set for Upstash-backed features (sessions, applications).</summary>
public interface IRedisStringStore
{
    Task<string?> StringGetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Batch GET via Upstash pipeline — one HTTP round-trip for many keys.</summary>
    Task<IReadOnlyList<string?>> StringGetManyAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    Task StringSetAsync(string key, string value, CancellationToken cancellationToken = default);

    Task StringDeleteAsync(string key, CancellationToken cancellationToken = default);
}
