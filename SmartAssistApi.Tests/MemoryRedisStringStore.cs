using System.Collections.Concurrent;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public sealed class MemoryRedisStringStore : IRedisStringStore
{
    private readonly ConcurrentDictionary<string, string> _data = new();

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_data.TryGetValue(key, out var v) ? v : null);
    }

    public Task SetAsync(string key, string value, int? ttlSeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _data[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _data.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
