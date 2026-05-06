using SmartAssistApi.Models;

namespace SmartAssistApi.Services.VectorStore;

public interface ICareerMemoryIngester
{
    /// <summary>Speichert Chunks aus einer abgeschlossenen Chat-Session.</summary>
    Task IngestConversationAsync(
        string userId,
        string toolType,
        string sessionId,
        string userMessage,
        string assistantReply,
        SessionContext context,
        string? jobApplicationId = null,
        CancellationToken ct = default);

    /// <summary>Speichert CV-Abschnitte als einzelne semantische Chunks.</summary>
    Task IngestCvAsync(string userId, string cvText, CancellationToken ct = default);

    /// <summary>Speichert strukturierte Job-Analyse-Ergebnisse.</summary>
    Task IngestJobAnalysisAsync(
        string userId,
        JobContext job,
        string analysisReply,
        string? jobApplicationId = null,
        CancellationToken ct = default);
}
