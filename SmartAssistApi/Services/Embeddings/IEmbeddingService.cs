namespace SmartAssistApi.Services.Embeddings;

public interface IEmbeddingService
{
    /// <summary>
    /// Erzeugt einen Embedding-Vektor für den gegebenen Text.
    /// </summary>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);

    /// <summary>
    /// Erzeugt Embeddings für mehrere Texte in einem Batch.
    /// </summary>
    Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default);

    /// <summary>Dimension des Embedding-Vektors (z.B. 384 für MiniLM, 1536 für text-embedding-3-small).</summary>
    int Dimension { get; }
}
