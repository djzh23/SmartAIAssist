using System.Threading.Channels;

namespace SmartAssistApi.Services.Background;

/// <summary>
/// Bounded queue for fire-and-forget work spawned from request handlers
/// (usage recording, insight extraction, career-memory ingestion, …).
/// Replaces the previous <c>Task.Run</c> pattern so background work is tracked,
/// backpressure-aware, and drained on graceful shutdown.
/// </summary>
public interface IAgentBackgroundQueue
{
    /// <summary>
    /// Enqueue a work item that will run inside a fresh scope. Returns true if accepted,
    /// false if the queue was full (the item is dropped; the caller's response is not affected).
    /// </summary>
    bool TryEnqueue(string name, Func<IServiceProvider, CancellationToken, Task> workItem);
}

public sealed class AgentBackgroundQueue : IAgentBackgroundQueue
{
    public const int Capacity = 500;

    private readonly Channel<QueuedWorkItem> _channel;
    private readonly ILogger<AgentBackgroundQueue> _logger;

    public AgentBackgroundQueue(ILogger<AgentBackgroundQueue> logger)
    {
        _logger = logger;
        _channel = Channel.CreateBounded<QueuedWorkItem>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool TryEnqueue(string name, Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (workItem is null)
            throw new ArgumentNullException(nameof(workItem));

        if (_channel.Writer.TryWrite(new QueuedWorkItem(name, workItem)))
            return true;

        _logger.LogWarning(
            "AgentBackgroundQueue: capacity {Capacity} exceeded; dropping work item {Name}.",
            Capacity,
            name);
        return false;
    }

    internal ChannelReader<QueuedWorkItem> Reader => _channel.Reader;

    internal void Complete() => _channel.Writer.TryComplete();

    internal sealed record QueuedWorkItem(string Name, Func<IServiceProvider, CancellationToken, Task> WorkItem);
}
