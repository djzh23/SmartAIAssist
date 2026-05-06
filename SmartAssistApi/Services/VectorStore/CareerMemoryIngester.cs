using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SmartAssistApi.Configuration;
using SmartAssistApi.Models;
using SmartAssistApi.Services.Embeddings;

namespace SmartAssistApi.Services.VectorStore;

public sealed class CareerMemoryIngester(
    IHttpClientFactory httpClientFactory,
    IEmbeddingService embeddings,
    ILlmSingleCompletionService singleCompletion,
    IOptions<QdrantOptions> qdrantOptions,
    ILogger<CareerMemoryIngester> logger) : ICareerMemoryIngester
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly QdrantOptions _qdrant = qdrantOptions.Value;

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
        if (!_qdrant.Enabled)
            return;

        var nuggets = await ExtractNuggetsAsync(userMessage, assistantReply, ct).ConfigureAwait(false);
        if (nuggets.Count == 0)
            return;

        var createdAt = DateTimeOffset.UtcNow;
        var jobTitle = context.Job?.JobTitle ?? context.InterviewJobTitle;
        var company = context.Job?.CompanyName ?? context.InterviewCompany;

        var chunks = nuggets
            .Select(content => new MemoryChunk(
                ChunkType: "conversation_summary",
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
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(cvText) || !_qdrant.Enabled)
            return;

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
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(analysisReply) || !_qdrant.Enabled)
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

    private static bool ShouldIngest(string userId, string userMessage, string assistantReply) =>
        !string.IsNullOrWhiteSpace(userId)
        && userMessage.Trim().Length >= 40
        && assistantReply.Trim().Length >= 80;

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

        var points = chunks.Select((chunk, idx) => new
        {
            id = Guid.NewGuid().ToString("D"),
            vector = vectors[idx],
            payload = new
            {
                user_id = userId,
                source_tool = sourceTool,
                chunk_type = chunk.ChunkType,
                content = chunk.Content,
                metadata = new
                {
                    session_id = chunk.SessionId,
                    job_title = chunk.JobTitle,
                    company = chunk.Company,
                    created_at = chunk.CreatedAt.ToString("O"),
                    job_application_id = chunk.JobApplicationId,
                },
            },
        }).ToArray();

        var body = JsonSerializer.Serialize(new { points }, JsonOptions);
        var client = httpClientFactory.CreateClient("qdrant");
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/collections/{_qdrant.CollectionName}/points?wait=false")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_qdrant.ApiKey))
            req.Headers.Add("api-key", _qdrant.ApiKey);

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Qdrant upsert failed ({(int)res.StatusCode}): {err}");
        }
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
