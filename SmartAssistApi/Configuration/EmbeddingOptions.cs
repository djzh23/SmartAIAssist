namespace SmartAssistApi.Configuration;

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    public string Provider { get; set; } = "onnx";

    public string ModelPath { get; set; } = "Models/model.onnx";

    public string VocabPath { get; set; } = "Models/vocab.txt";

    public int Dimension { get; set; } = 384;

    public int MaxSequenceLength { get; set; } = 128;

    public int IntraOpThreads { get; set; } = 2;

    public int InterOpThreads { get; set; } = 1;
}
