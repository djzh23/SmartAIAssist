namespace SmartAssistApi.Services;

/// <summary>
/// Background service that evicts conversation history and context
/// for sessions inactive for more than 2 hours, preventing unbounded
/// memory growth in long-running deployments.
/// </summary>
public sealed class ConversationCleanupService(
    ConversationService conversationService,
    ILogger<ConversationCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval  = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxAge    = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Conversation cleanup service started (interval {Interval}, maxAge {MaxAge})", Interval, MaxAge);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);

            try
            {
                await conversationService.CleanupOldSessionsAsync(MaxAge);
                logger.LogDebug("Conversation cleanup completed");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Conversation cleanup failed");
            }
        }
    }
}
