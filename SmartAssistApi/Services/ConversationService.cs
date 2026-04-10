using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public class ConversationService
{
    /// <summary>Sliding window of user/assistant turns sent to the model (input cost control).</summary>
    public const int MaxHistoryMessages = 6;

    private readonly Dictionary<string, List<Message>> _histories = new();
    private readonly Dictionary<string, SessionContext> _contexts = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static string HistoryKey(string sessionId, string toolType) =>
        $"{toolType}:{sessionId}";

    public async Task<List<Message>> GetHistoryAsync(string sessionId, string toolType)
    {
        await _lock.WaitAsync();
        try
        {
            var key = HistoryKey(sessionId, toolType);
            if (!_histories.ContainsKey(key))
                _histories[key] = new List<Message>();

            return new List<Message>(_histories[key]);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveHistoryAsync(string sessionId, string toolType, List<Message> messages)
    {
        await _lock.WaitAsync();
        try
        {
            var key = HistoryKey(sessionId, toolType);
            _histories[key] = messages.TakeLast(MaxHistoryMessages).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SessionContext> GetContextAsync(string sessionId, string toolType)
    {
        await _lock.WaitAsync();
        try
        {
            var key = HistoryKey(sessionId, toolType);
            if (!_contexts.ContainsKey(key))
            {
                _contexts[key] = new SessionContext
                {
                    SessionId = sessionId,
                    ToolType = toolType,
                };
            }

            return _contexts[key];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateContextAsync(string sessionId, string toolType, Action<SessionContext> update)
    {
        await _lock.WaitAsync();
        try
        {
            var key = HistoryKey(sessionId, toolType);
            if (!_contexts.ContainsKey(key))
            {
                _contexts[key] = new SessionContext
                {
                    SessionId = sessionId,
                    ToolType = toolType,
                };
            }

            update(_contexts[key]);
            _contexts[key].LastActivity = DateTime.UtcNow;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearSessionAsync(string sessionId, string toolType)
    {
        await _lock.WaitAsync();
        try
        {
            var key = HistoryKey(sessionId, toolType);
            _histories.Remove(key);
            _contexts.Remove(key);
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
        }
        finally
        {
            _lock.Release();
        }
    }
}
