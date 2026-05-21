namespace SmartAssistApi.Services.Background;

/// <summary>
/// Drains <see cref="AgentBackgroundQueue"/> on a single background loop.
/// Each work item runs inside its own DI scope so scoped services (DbContext, etc.) are isolated.
/// </summary>
public sealed class AgentBackgroundQueueProcessor(
    AgentBackgroundQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<AgentBackgroundQueueProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AgentBackgroundQueueProcessor: started.");

        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunSafelyAsync(item, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // graceful shutdown
        }
        catch (Exception ex)
        {
            // The loop itself should never crash; if it does, the host restarts.
            logger.LogError(ex, "AgentBackgroundQueueProcessor: loop terminated unexpectedly.");
            throw;
        }
        finally
        {
            logger.LogInformation("AgentBackgroundQueueProcessor: stopped.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new work and let ExecuteAsync drain the channel before the host kills it.
        queue.Complete();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunSafelyAsync(AgentBackgroundQueue.QueuedWorkItem item, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await item.WorkItem(scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown — swallow
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Background work item {Name} failed.", item.Name);
        }
    }
}
