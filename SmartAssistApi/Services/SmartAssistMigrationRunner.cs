using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;

namespace SmartAssistApi.Services;

/// <summary>
/// Applies SmartAssist SQL migrations (Migrations/NNN_*.sql) idempotently at startup.
/// Tracks applied files in a metadata table so subsequent deploys are no-ops.
///
/// SQL files are embedded resources (see SmartAssistApi.csproj &lt;EmbeddedResource&gt;).
/// Files are applied in ascending numeric prefix order inside a transaction;
/// if any file fails, the run aborts and the app refuses to start so we never serve
/// requests against a partially-migrated schema.
/// </summary>
public sealed class SmartAssistMigrationRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<SmartAssistMigrationRunner> logger) : IHostedService
{
    private const string TrackingTableSql = """
        CREATE TABLE IF NOT EXISTS __smartassist_migrations (
            migration_id TEXT PRIMARY KEY,
            applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    private static readonly Regex NumericPrefix = new(@"^(\d+)_", RegexOptions.Compiled);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<SmartAssistDbContext>();
        if (db is null)
        {
            logger.LogInformation(
                "SmartAssist migrations: SmartAssistDbContext not registered (no Postgres). Skipping.");
            return;
        }

        var migrations = LoadEmbeddedMigrations();
        if (migrations.Count == 0)
        {
            logger.LogWarning(
                "SmartAssist migrations: no embedded SQL files found in Migrations/. " +
                "Check that *.sql files are included as EmbeddedResource in the csproj.");
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(TrackingTableSql, cancellationToken).ConfigureAwait(false);

            var applied = await LoadAppliedSetAsync(db, cancellationToken).ConfigureAwait(false);
            var pending = migrations.Where(m => !applied.Contains(m.Id)).ToList();

            if (pending.Count == 0)
            {
                logger.LogInformation(
                    "SmartAssist migrations: schema up to date ({Count} files already applied).",
                    migrations.Count);
                return;
            }

            logger.LogInformation(
                "SmartAssist migrations: applying {Pending} of {Total} migrations.",
                pending.Count,
                migrations.Count);

            foreach (var migration in pending)
            {
                await ApplyAsync(db, migration, cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation(
                "SmartAssist migrations: applied {Pending} migration(s) successfully.",
                pending.Count);
        }
        catch (Exception ex)
        {
            logger.LogCritical(
                ex,
                "SmartAssist migrations: failed. Fix Postgres permissions/connection; " +
                "SmartAssist tables will be missing/incomplete until migrations succeed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static List<EmbeddedMigration> LoadEmbeddedMigrations()
    {
        var asm = typeof(SmartAssistMigrationRunner).Assembly;
        var names = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Migrations.", StringComparison.Ordinal)
                && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var list = new List<EmbeddedMigration>(names.Count);
        foreach (var resourceName in names)
        {
            var lastDot = resourceName.LastIndexOf('.', resourceName.Length - 5);
            var fileName = lastDot >= 0
                ? resourceName[(lastDot + 1)..]
                : resourceName;

            var match = NumericPrefix.Match(fileName);
            if (!match.Success)
                continue;

            var order = int.Parse(match.Groups[1].Value);
            list.Add(new EmbeddedMigration(fileName, order, resourceName, asm));
        }

        return list.OrderBy(m => m.Order).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    private static async Task<HashSet<string>> LoadAppliedSetAsync(
        SmartAssistDbContext db,
        CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT migration_id FROM __smartassist_migrations;";

        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            set.Add(reader.GetString(0));
        }

        return set;
    }

    private async Task ApplyAsync(
        SmartAssistDbContext db,
        EmbeddedMigration migration,
        CancellationToken cancellationToken)
    {
        var sql = migration.ReadAllText();
        if (string.IsNullOrWhiteSpace(sql))
        {
            logger.LogWarning(
                "SmartAssist migrations: {Id} is empty; recording as applied to skip in future.",
                migration.Id);
        }

        await using var tx = await db.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(sql))
            {
                await db.Database
                    .ExecuteSqlRawAsync(sql, cancellationToken)
                    .ConfigureAwait(false);
            }

            await db.Database
                .ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO __smartassist_migrations (migration_id) VALUES ({migration.Id}) ON CONFLICT DO NOTHING;",
                    cancellationToken)
                .ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("SmartAssist migrations: applied {Id}.", migration.Id);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private sealed record EmbeddedMigration(string Id, int Order, string ResourceName, Assembly Assembly)
    {
        public string ReadAllText()
        {
            using var stream = Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded migration resource not found: {ResourceName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
