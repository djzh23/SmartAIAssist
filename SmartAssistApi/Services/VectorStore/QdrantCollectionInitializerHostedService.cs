namespace SmartAssistApi.Services.VectorStore;

public sealed class QdrantCollectionInitializerHostedService(
    QdrantCollectionInitializer initializer,
    ILogger<QdrantCollectionInitializerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await initializer.EnsureCollectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant collection initialization failed. Continuing without vector store.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
