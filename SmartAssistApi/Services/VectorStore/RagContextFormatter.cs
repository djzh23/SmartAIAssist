using System.Text;

namespace SmartAssistApi.Services.VectorStore;

public static class RagContextFormatter
{
    private const int MaxRagContextChars = 1500;

    public static string FormatForSystemPrompt(List<RetrievedMemory> memories)
    {
        if (memories.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("[KARRIERE-GEDÄCHTNIS — relevante Erkenntnisse aus früheren Sessions]");

        var totalChars = 0;
        foreach (var memory in memories.OrderByDescending(m => m.Score))
        {
            var line = $"• [{memory.ChunkType}] {memory.Content}";
            if (!string.IsNullOrWhiteSpace(memory.JobTitle))
                line += $" (Stelle: {memory.JobTitle}";
            if (!string.IsNullOrWhiteSpace(memory.Company))
                line += $" bei {memory.Company}";
            if (!string.IsNullOrWhiteSpace(memory.JobTitle))
                line += ")";

            if (totalChars + line.Length > MaxRagContextChars)
                break;

            sb.AppendLine(line);
            totalChars += line.Length;
        }

        sb.AppendLine("[ENDE KARRIERE-GEDÄCHTNIS]");
        sb.AppendLine("Verwende diese Erkenntnisse nur wenn sie zur aktuellen Frage passen. Erfinde keine Verbindungen.");
        return sb.ToString();
    }
}
