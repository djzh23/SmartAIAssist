namespace SmartAssistApi.Configuration;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    public string Provider { get; set; } = "openai";

    public string OpenAiApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "text-embedding-3-small";

    public int Dimension { get; set; } = 1536;

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public int TimeoutSeconds { get; set; } = 30;
}
