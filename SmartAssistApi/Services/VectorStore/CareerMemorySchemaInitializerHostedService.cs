using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;

namespace SmartAssistApi.Services.VectorStore;

public sealed class CareerMemorySchemaInitializerHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CareerMemorySchemaInitializerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetService<SmartAssistDbContext>();
            if (db is null)
            {
                logger.LogInformation("Skipping career_memory schema init (no Postgres DbContext).");
                return;
            }

            const string sql = """
                CREATE EXTENSION IF NOT EXISTS vector;

                CREATE TABLE IF NOT EXISTS career_memory (
                    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                    user_id TEXT NOT NULL,
                    source_tool TEXT NOT NULL,
                    chunk_type TEXT NOT NULL,
                    content TEXT NOT NULL,
                    embedding vector(384) NOT NULL,
                    session_id TEXT NULL,
                    job_title TEXT NULL,
                    company TEXT NULL,
                    job_application_id TEXT NULL,
                    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    content_hash TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_career_memory_user
                    ON career_memory (user_id);

                CREATE INDEX IF NOT EXISTS idx_career_memory_embedding
                    ON career_memory USING ivfflat (embedding vector_cosine_ops)
                    WITH (lists = 50);

                CREATE UNIQUE INDEX IF NOT EXISTS idx_career_memory_dedup
                    ON career_memory (user_id, content_hash);
                """;

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("career_memory schema ensured (pgvector).");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "career_memory schema initialization failed. Continuing startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
