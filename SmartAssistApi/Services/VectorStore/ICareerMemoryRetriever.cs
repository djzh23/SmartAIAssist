namespace SmartAssistApi.Services.VectorStore;

public interface ICareerMemoryRetriever
{
    /// <summary>
    /// Sucht die relevantesten Wissens-Chunks für eine User-Query.
    /// </summary>
    Task<List<RetrievedMemory>> RetrieveAsync(
        string userId,
        string query,
        RetrievalFilter filter,
        int topK = 5,
        float minScore = 0.65f,
        CancellationToken ct = default);
}

public sealed record RetrievedMemory(
    string Content,
    string ChunkType,
    string SourceTool,
    float Score,
    DateTimeOffset CreatedAt,
    string? JobTitle = null,
    string? Company = null);

public sealed record RetrievalFilter(
    string? SourceTool = null,
    string? ChunkType = null,
    string? JobApplicationId = null);
