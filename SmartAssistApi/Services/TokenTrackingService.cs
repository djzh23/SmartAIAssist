using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartAssistApi.Data;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Where token usage is actually stored vs what was configured.</summary>
public readonly record struct TokenTrackingBackendInfo(
    string EffectiveStorage,
    string ConfiguredTokenUsageStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>
/// Routes token usage metrics to Redis or PostgreSQL based on <see cref="DatabaseFeatureOptions"/>.
/// </summary>
public class TokenTrackingService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    TokenTrackingRedisService redis,
    IServiceProvider serviceProvider)
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private TokenTrackingPostgresService? Postgres =>
        serviceProvider.GetService(typeof(TokenTrackingPostgresService)) as TokenTrackingPostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.TokenUsageStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    /// <summary>Used for response headers and client-visible degraded-mode disclosure.</summary>
    public virtual TokenTrackingBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.TokenUsageStorage)
            ? "redis"
            : _opts.TokenUsageStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new TokenTrackingBackendInfo(effective, configured, degraded, reason);
    }

    public static decimal CalculateCost(
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0) =>
        TokenTrackingCostHelper.CalculateCost(model, inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens);

    public static string? ParseTopToolFromTcFields(Dictionary<string, string> map) =>
        TokenTrackingCostHelper.ParseTopToolFromTcFields(map);

    public virtual Task TrackUsageAsync(
        string userId,
        string toolType,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0) =>
        UsePostgres
            ? Postgres!.TrackUsageAsync(
                userId,
                toolType,
                model,
                inputTokens,
                outputTokens,
                cacheCreationInputTokens,
                cacheReadInputTokens,
                CancellationToken.None)
            : redis.TrackUsageAsync(userId, toolType, model, inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens);

    public virtual Task<AdminDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default) =>
        UsePostgres ? Postgres!.GetDashboardDataAsync(cancellationToken) : redis.GetDashboardDataAsync(cancellationToken);

    public virtual Task<UserUsageSummary> GetUserUsageAsync(
        string userId,
        string startDate,
        string endDate,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetUserUsageAsync(userId, startDate, endDate, cancellationToken)
            : redis.GetUserUsageAsync(userId, startDate, endDate, cancellationToken);

    public virtual Task<List<UserUsageSummary>> GetTopUsersAsync(string date, int limit = 20, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetTopUsersAsync(date, limit, cancellationToken)
            : redis.GetTopUsersAsync(date, limit, cancellationToken);

    public virtual Task<List<UserUsageSummary>> GetTopUsersDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetTopUsersDateRangeAsync(startUtc, endUtc, limit, cancellationToken)
            : redis.GetTopUsersDateRangeAsync(startUtc, endUtc, limit, cancellationToken);

    public virtual Task<List<UserUsageSummary>> GetTopUsersForDateRangeQueryAsync(
        string startDate,
        string endDate,
        int limit,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetTopUsersForDateRangeQueryAsync(startDate, endDate, limit, cancellationToken)
            : redis.GetTopUsersForDateRangeQueryAsync(startDate, endDate, limit, cancellationToken);

    public virtual Task<List<DailyUsage>> GetDailyStatsAsync(int days, CancellationToken cancellationToken = default) =>
        UsePostgres ? Postgres!.GetDailyStatsAsync(days, cancellationToken) : redis.GetDailyStatsAsync(days, cancellationToken);
}
