namespace SmartAssistApi.Configuration;

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://localhost:6333";

    public string? ApiKey { get; set; }

    public string CollectionName { get; set; } = "career_memory";
}
