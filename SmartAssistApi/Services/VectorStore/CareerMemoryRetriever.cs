using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SmartAssistApi.Data;
using SmartAssistApi.Services.Embeddings;

namespace SmartAssistApi.Services.VectorStore;

public sealed class CareerMemoryRetriever(
    SmartAssistDbContext db,
    IEmbeddingService embeddings,
    ILogger<CareerMemoryRetriever> logger) : ICareerMemoryRetriever
{
    public async Task<List<RetrievedMemory>> RetrieveAsync(
        string userId,
        string query,
        RetrievalFilter filter,
        int topK = 5,
        float minScore = 0.65f,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(query))
            return [];

        var queryVector = await embeddings.EmbedAsync(query, ct).ConfigureAwait(false);
        var vectorLiteral = BuildVectorLiteral(queryVector);

        var clauses = new List<string> { "user_id = @userId" };
        var parameters = new List<NpgsqlParameter>
        {
            new("userId", userId),
            new("vector", vectorLiteral),
            new("topK", Math.Clamp(topK, 1, 20)),
        };

        if (!string.IsNullOrWhiteSpace(filter.SourceTool))
        {
            clauses.Add("source_tool = @sourceTool");
            parameters.Add(new NpgsqlParameter("sourceTool", filter.SourceTool.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(filter.ChunkType))
        {
            clauses.Add("chunk_type = @chunkType");
            parameters.Add(new NpgsqlParameter("chunkType", filter.ChunkType.Trim()));
        }
        if (!string.IsNullOrWhiteSpace(filter.JobApplicationId))
        {
            clauses.Add("job_application_id = @jobApplicationId");
            parameters.Add(new NpgsqlParameter("jobApplicationId", filter.JobApplicationId.Trim()));
        }

        var whereSql = string.Join(" AND ", clauses);
        var sql = $"""
            SELECT content, chunk_type, source_tool, created_at, job_title, company,
                   CAST(1 - (embedding <=> CAST(@vector AS vector)) AS real) AS score
            FROM career_memory
            WHERE {whereSql}
            ORDER BY embedding <=> CAST(@vector AS vector)
            LIMIT @topK
            """;

        var rows = new List<RetrievedMemory>();
        try
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct).ConfigureAwait(false);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.AddRange(parameters.ToArray());

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var score = reader.GetFloat(reader.GetOrdinal("score"));
                if (score < minScore)
                    continue;

                rows.Add(new RetrievedMemory(
                    Content: reader.GetString(reader.GetOrdinal("content")),
                    ChunkType: reader.GetString(reader.GetOrdinal("chunk_type")),
                    SourceTool: reader.GetString(reader.GetOrdinal("source_tool")),
                    Score: score,
                    CreatedAt: reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at")),
                    JobTitle: reader.IsDBNull(reader.GetOrdinal("job_title")) ? null : reader.GetString(reader.GetOrdinal("job_title")),
                    Company: reader.IsDBNull(reader.GetOrdinal("company")) ? null : reader.GetString(reader.GetOrdinal("company"))));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "pgvector retrieval failed for user {UserId}", userId);
            return [];
        }

        return rows.OrderByDescending(r => r.Score).ToList();
    }

    private static string BuildVectorLiteral(IReadOnlyList<float> vector)
    {
        var sb = new StringBuilder();
        sb.Append('[');
        for (var i = 0; i < vector.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(vector[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }
}
