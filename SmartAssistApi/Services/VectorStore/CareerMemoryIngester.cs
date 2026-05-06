using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;
using SmartAssistApi.Services.Embeddings;

namespace SmartAssistApi.Services.VectorStore;

public sealed class CareerMemoryIngester(
    SmartAssistDbContext db,
    IEmbeddingService embeddings,
    ILlmSingleCompletionService singleCompletion,
    ILogger<CareerMemoryIngester> logger) : ICareerMemoryIngester
{
    public async Task IngestConversationAsync(
        string userId,
        string toolType,
        string sessionId,
        string userMessage,
        string assistantReply,
        SessionContext context,
        string? jobApplicationId = null,
        CancellationToken ct = default)
    {
        if (!ShouldIngest(userId, userMessage, assistantReply))
            return;

        var nuggets = await ExtractNuggetsAsync(userMessage, assistantReply, ct).ConfigureAwait(false);
        if (nuggets.Count == 0)
            return;

        var createdAt = DateTimeOffset.UtcNow;
        var jobTitle = context.Job?.JobTitle ?? context.InterviewJobTitle;
        var company = context.Job?.CompanyName ?? context.InterviewCompany;

        var chunks = nuggets
            .Select(content => new MemoryChunk(
                ChunkType: DetermineChunkType(content, toolType),
                Content: content,
                JobTitle: NormalizeNullable(jobTitle),
                Company: NormalizeNullable(company),
                SessionId: sessionId,
                JobApplicationId: NormalizeNullable(jobApplicationId),
                CreatedAt: createdAt))
            .ToList();

        await UpsertChunksAsync(userId, NormalizeToolType(toolType), chunks, ct).ConfigureAwait(false);
    }

    public async Task IngestCvAsync(string userId, string cvText, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(cvText))
            return;

        await db.CareerMemory
            .Where(x => x.UserId == userId && x.SourceTool == "cv_upload")
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        var chunks = BuildCvChunks(cvText)
            .Select(text => new MemoryChunk(
                ChunkType: "cv_section",
                Content: text,
                JobTitle: null,
                Company: null,
                SessionId: "cv_profile",
                JobApplicationId: null,
                CreatedAt: DateTimeOffset.UtcNow))
            .ToList();

        if (chunks.Count == 0)
            return;

        await UpsertChunksAsync(userId, "cv_upload", chunks, ct).ConfigureAwait(false);
    }

    public async Task IngestJobAnalysisAsync(
        string userId,
        JobContext job,
        string analysisReply,
        string? jobApplicationId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(analysisReply))
            return;

        var chunks = BuildJobAnalysisChunks(analysisReply)
            .Select(section => new MemoryChunk(
                ChunkType: "job_analysis",
                Content: section,
                JobTitle: NormalizeNullable(job.JobTitle),
                Company: NormalizeNullable(job.CompanyName),
                SessionId: "job_analysis",
                JobApplicationId: NormalizeNullable(jobApplicationId),
                CreatedAt: DateTimeOffset.UtcNow))
            .ToList();

        if (chunks.Count == 0)
            return;

        await UpsertChunksAsync(userId, "jobanalyzer", chunks, ct).ConfigureAwait(false);
    }

    public async Task IngestInsightAsync(string userId, LearningInsight insight, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(insight.Content))
            return;

        var chunkType = insight.Category switch
        {
            "skill_gap" => "skill_gap",
            "action_item" => "action_item",
            _ => "conversation_insight",
        };
        var content = string.IsNullOrWhiteSpace(insight.Title)
            ? insight.Content.Trim()
            : $"{insight.Title.Trim()}: {insight.Content.Trim()}";

        var chunks = new List<MemoryChunk>
        {
            new(
                ChunkType: chunkType,
                Content: content,
                JobTitle: null,
                Company: null,
                SessionId: "learning_insight",
                JobApplicationId: NormalizeNullable(insight.JobApplicationId),
                CreatedAt: insight.CreatedAt == default ? DateTimeOffset.UtcNow : insight.CreatedAt),
        };
        await UpsertChunksAsync(userId, NormalizeToolType(insight.SourceTool ?? "general"), chunks, ct).ConfigureAwait(false);
    }

    public async Task IngestProfileAsync(string userId, CareerProfile profile, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await db.CareerMemory
            .Where(x => x.UserId == userId && x.SourceTool == "career_profile")
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        var chunks = new List<MemoryChunk>();
        if (profile.Skills.Count > 0)
            chunks.Add(new MemoryChunk(
                ChunkType: "cv_section",
                Content: $"Skills: {string.Join(", ", profile.Skills.Take(40))}",
                JobTitle: null,
                Company: null,
                SessionId: "profile",
                JobApplicationId: null,
                CreatedAt: DateTimeOffset.UtcNow));

        foreach (var exp in profile.Experience.Take(10))
        {
            var line = string.Join(" — ", new[] { exp.Title, exp.Company, exp.Summary }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (line.Trim().Length < 24)
                continue;
            chunks.Add(new MemoryChunk(
                ChunkType: "cv_section",
                Content: $"Erfahrung: {line.Trim()}",
                JobTitle: null,
                Company: null,
                SessionId: "profile",
                JobApplicationId: null,
                CreatedAt: DateTimeOffset.UtcNow));
        }

        if (profile.Goals.Count > 0)
        {
            chunks.Add(new MemoryChunk(
                ChunkType: "action_item",
                Content: $"Karriereziele: {string.Join(", ", profile.Goals.Take(12))}",
                JobTitle: null,
                Company: null,
                SessionId: "profile",
                JobApplicationId: null,
                CreatedAt: DateTimeOffset.UtcNow));
        }

        if (chunks.Count == 0)
            return;

        await UpsertChunksAsync(userId, "career_profile", chunks, ct).ConfigureAwait(false);
    }

    private static bool ShouldIngest(string userId, string userMessage, string assistantReply) =>
        !string.IsNullOrWhiteSpace(userId)
        && userMessage.Trim().Length >= 80
        && assistantReply.Trim().Length >= 200;

    private async Task<List<string>> ExtractNuggetsAsync(string userMessage, string assistantReply, CancellationToken ct)
    {
        var prompt = $"""
            Extrahiere aus diesem Chat-Turn die 1-3 wichtigsten karriere-relevanten Fakten, Empfehlungen oder Erkenntnisse.
            Format:
            - Eine Erkenntnis pro Zeile
            - Maximal 100 Wörter pro Zeile
            - Nur Substanz, kein Smalltalk

            USER:
            {TrimForPrompt(userMessage, 2000)}

            ASSISTANT:
            {TrimForPrompt(assistantReply, 3000)}
            """;

        try
        {
            var raw = await singleCompletion.CompleteAsync(prompt, 400, ct).ConfigureAwait(false);
            var parsed = raw
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => Regex.Replace(l, @"^\s*[-*•\d\.\)]\s*", string.Empty).Trim())
                .Where(l => l.Length >= 24)
                .Take(3)
                .ToList();

            return parsed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Memory nugget extraction failed.");
            return [];
        }
    }

    private async Task UpsertChunksAsync(
        string userId,
        string sourceTool,
        IReadOnlyList<MemoryChunk> chunks,
        CancellationToken ct)
    {
        if (chunks.Count == 0)
            return;

        var vectors = await embeddings
            .EmbedBatchAsync(chunks.Select(c => c.Content).ToArray(), ct)
            .ConfigureAwait(false);

        var entities = new List<CareerMemoryChunkEntity>();
        for (var idx = 0; idx < chunks.Count; idx++)
        {
            var chunk = chunks[idx];
            var hash = ComputeHash(chunk.Content);
            var exists = await db.CareerMemory
                .AnyAsync(x => x.UserId == userId && x.ContentHash == hash, ct)
                .ConfigureAwait(false);
            if (exists)
                continue;

            entities.Add(new CareerMemoryChunkEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SourceTool = sourceTool,
                ChunkType = chunk.ChunkType,
                Content = chunk.Content,
                Embedding = new Vector(vectors[idx]),
                SessionId = chunk.SessionId,
                JobTitle = chunk.JobTitle,
                Company = chunk.Company,
                JobApplicationId = chunk.JobApplicationId,
                CreatedAt = chunk.CreatedAt.UtcDateTime,
                ContentHash = hash,
            });
        }

        if (entities.Count == 0)
            return;

        db.CareerMemory.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static IEnumerable<string> BuildCvChunks(string cvText)
    {
        var normalized = cvText.Replace("\r\n", "\n", StringComparison.Ordinal);
        var sections = Regex.Split(normalized, @"\n(?=(erfahrung|experience|ausbildung|education|skills|kenntnisse|sprachen|languages)\b)", RegexOptions.IgnoreCase);
        var chunks = sections
            .Select(s => s.Trim())
            .Where(s => s.Length >= 80)
            .Select(s => s.Length > 2200 ? s[..2200] : s)
            .Take(8)
            .ToList();

        if (chunks.Count > 0)
            return chunks;

        return normalized.Length <= 3500
            ? [normalized.Trim()]
            : [normalized[..1700].Trim(), normalized[1700..Math.Min(normalized.Length, 3400)].Trim()];
    }

    private static IEnumerable<string> BuildJobAnalysisChunks(string analysisReply)
    {
        var parts = Regex.Split(analysisReply, @"\n(?=##\s+)", RegexOptions.Multiline)
            .Select(p => p.Trim())
            .Where(p => p.Length >= 60)
            .Take(4)
            .ToList();

        if (parts.Count > 0)
            return parts;

        var compact = analysisReply.Trim();
        if (compact.Length <= 1800)
            return [compact];
        return [compact[..1800]];
    }

    private static string TrimForPrompt(string value, int maxChars)
    {
        var trimmed = value.Trim();
        return trimmed.Length > maxChars ? trimmed[..maxChars] : trimmed;
    }

    private static string NormalizeToolType(string toolType) =>
        string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.Trim().ToLowerInvariant();

    private static string DetermineChunkType(string content, string toolType)
    {
        var lower = content.ToLowerInvariant();
        if (lower.Contains("lücke") || lower.Contains("fehlt") || lower.Contains("gap"))
            return "skill_gap";
        if (lower.Contains("nächster schritt") || lower.Contains("aktion") || lower.Contains("todo"))
            return "action_item";
        if (NormalizeToolType(toolType) == "jobanalyzer")
            return "job_analysis";
        return "conversation_insight";
    }

    private static string ComputeHash(string content)
    {
        var normalized = content.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16];
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return value.Trim();
    }

    private sealed record MemoryChunk(
        string ChunkType,
        string Content,
        string? JobTitle,
        string? Company,
        string SessionId,
        string? JobApplicationId,
        DateTimeOffset CreatedAt);
}
