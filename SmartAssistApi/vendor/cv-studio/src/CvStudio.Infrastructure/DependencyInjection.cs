using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using CvStudio.Application.Repositories;
using CvStudio.Application.Services;
using CvStudio.Infrastructure.Persistence;
using CvStudio.Infrastructure.Docx;
using CvStudio.Infrastructure.Pdf;
using CvStudio.Infrastructure.Repositories;

namespace CvStudio.Infrastructure;

public static class DependencyInjection
{
    private const string PostgresConnectionName = "Postgres";
    private const string DefaultConnectionString = "Host=localhost;Port=5432;Database=resumeversioner;Username=postgres;Password=postgres";
    private const string PostgresAuthenticationFailedSqlState = "28P01";
    private const string StartupMigrationLoggerName = "StartupMigration";
    private const string PostgresDatabaseName = "postgres";
    private const string DatabaseExistsQuery = "SELECT 1 FROM pg_database WHERE datname = @db";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? DefaultConnectionString;

        services.AddDbContext<CvStudioDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<CvStudioDbContext>());
        services.AddScoped<IResumeRepository, ResumeRepository>();
        services.AddScoped<ISnapshotRepository, SnapshotRepository>();
        services.AddScoped<IPdfGenerator, QuestPdfGenerator>();
        services.AddScoped<IDocxGenerator, OpenXmlDocxGenerator>();

        return services;
    }

    public static async Task ApplyMigrationsAsync(this IServiceProvider services, bool isDevelopment)
    {
        if (!isDevelopment)
        {
            return;
        }

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CvStudioDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(StartupMigrationLoggerName);

        try
        {
            await EnsureDatabaseExistsAsync(dbContext, logger);
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresAuthenticationFailedSqlState)
        {
            logger.LogError(
                ex,
                "PostgreSQL authentication failed. Check ConnectionStrings:Postgres (or ConnectionStrings__Postgres env var). API will start, but DB-backed endpoints will fail until credentials are fixed.");
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(
                ex,
                "PostgreSQL is unreachable or misconfigured. API will start, but DB-backed endpoints will fail until the database is available.");
        }
    }

    private static async Task EnsureDatabaseExistsAsync(CvStudioDbContext dbContext, ILogger logger)
    {
        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var targetDatabase = builder.Database;
        if (string.IsNullOrWhiteSpace(targetDatabase))
        {
            return;
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = PostgresDatabaseName
        };

        await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
        await connection.OpenAsync();

        await using var existsCommand = connection.CreateCommand();
        existsCommand.CommandText = DatabaseExistsQuery;
        existsCommand.Parameters.AddWithValue("@db", targetDatabase);

        var exists = await existsCommand.ExecuteScalarAsync();
        if (exists is not null)
        {
            return;
        }

        // Quote identifier defensively to handle uncommon database names.
        var safeDbName = targetDatabase.Replace("\"", "\"\"");
        await using var createCommand = connection.CreateCommand();
        createCommand.CommandText = $"CREATE DATABASE \"{safeDbName}\"";
        await createCommand.ExecuteNonQueryAsync();

        logger.LogInformation("Created missing PostgreSQL database {DatabaseName}.", targetDatabase);
    }
}

