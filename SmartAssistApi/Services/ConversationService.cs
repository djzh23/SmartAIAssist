using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public class ConversationService
{
    /// <summary>Sliding window of user/assistant turns sent to the model (input cost control).</summary>
    public const int MaxHistoryMessages = 6;

    /// <summary>
    /// Hard cap on resident sessions to prevent unbounded memory growth between cleanup ticks
    /// (audit finding #13). Hitting the cap triggers an immediate eviction of the oldest entries
    /// by LastActivity; the 2-hour scheduled cleanup still runs as before.
    /// </summary>
    public const int MaxActiveSessions = 10_000;

    /// <summary>Once <see cref="MaxActiveSessions"/> is reached, evict in batches so we don't
    /// thrash on every new session in a burst.</summary>
    private const int EvictionBatchSize = 1_000;

    private readonly Dictionary<string, List<Message>> _histories = new();
    private readonly Dictionary<string, SessionContext> _contexts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Current resident session count. Read with <see cref="Volatile"/> semantics so it's
    /// safe to call from a health check / metrics endpoint without taking the semaphore.</summary>
    private int _activeSessionCount;
    public int ActiveSessionCount => Volatile.Read(ref _activeSessionCount);

    /// <summary>Scope id may contain ':' (e.g. demo keys) — use a non-printable separator.</summary>
    private static string StorageKey(string scopeUserId, string sessionId, string toolType) =>
        $"{scopeUserId}\u001f{toolType}\u001f{sessionId}";

    public async Task<List<Message>> GetHistoryAsync(string scopeUserId, string sessionId, string toolType)
    {
        await _lock.WaitAsync();
        try
        {
            var key = StorageKey(scopeUserId, sessionId, toolType);
            if (!_histories.ContainsKey(key))
                _histories[key] = new List<Message>();

            return new List<Message>(_histories[key]);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Caller already holds <see cref="_lock"/>. Drops the oldest <see cref="EvictionBatchSize"/>
    /// sessions by LastActivity when the dictionary is at the cap. Both dictionaries stay in lockstep.
    /// </summary>
    private void EvictOldestIfOverCap()
    {
        if (_contexts.Count < MaxActiveSessions)
            return;

        var oldest = _contexts
            .OrderBy(kv => kv.Value.LastActivity)
            .Take(EvictionBatchSize)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in oldest)
        {
            _contexts.Remove(key);
            _histories.Remove(key);
        }
        Volatile.Write(ref _activeSessionCount, _contexts.Count);
    }

    public async Task SaveHistoryAsync(string scopeUserId, string sessionId, string toolType, List<Message> messages)
    {
        await _lock.WaitAsync();
        try
        {
            var key = StorageKey(scopeUserId, sessionId, toolType);
            _histories[key] = messages.TakeLast(MaxHistoryMessages).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SessionContext> GetContextAsync(string scopeUserId, string sessionId, string toolType)
    {
        await _lock.WaitAsync();
        try
        {
            var key = StorageKey(scopeUserId, sessionId, toolType);
            if (!_contexts.ContainsKey(key))
            {
                EvictOldestIfOverCap();
                _contexts[key] = new SessionContext
                {
                    SessionId = sessionId,
                    ToolType = toolType,
                };
                Volatile.Write(ref _activeSessionCount, _contexts.Count);
            }

            return _contexts[key];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateContextAsync(string scopeUserId, string sessionId, string toolType, Action<SessionContext> update)
    {
        await _lock.WaitAsync();
        try
        {
            var key = StorageKey(scopeUserId, sessionId, toolType);
            if (!_contexts.ContainsKey(key))
            {
                EvictOldestIfOverCap();
                _contexts[key] = new SessionContext
                {
                    SessionId = sessionId,
                    ToolType = toolType,
                };
                Volatile.Write(ref _activeSessionCount, _contexts.Count);
            }

            update(_contexts[key]);
            _contexts[key].LastActivity = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearSessionAsync(string scopeUserId, string sessionId, string toolType)
    {
        await _lock.WaitAsync();
        try
        {
            var key = StorageKey(scopeUserId, sessionId, toolType);
            _histories.Remove(key);
            if (_contexts.Remove(key))
                Volatile.Write(ref _activeSessionCount, _contexts.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task CleanupOldSessionsAsync(TimeSpan maxAge)
    {
        await _lock.WaitAsync();
        try
        {
            var expiredKeys = _contexts
                .Where(kv => DateTime.UtcNow - kv.Value.LastActivity > maxAge)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _contexts.Remove(key);
                _histories.Remove(key);
            }
            Volatile.Write(ref _activeSessionCount, _contexts.Count);
        }
        finally
        {
            _lock.Release();
        }
    }
}
