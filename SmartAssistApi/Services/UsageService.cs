using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartAssistApi.Data;

namespace SmartAssistApi.Services;

/// <summary>Where daily usage limits + plan rows are stored vs what was configured.</summary>
public readonly record struct UsageBackendInfo(
    string EffectiveStorage,
    string ConfiguredUsageStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>
/// Routes daily usage and plan to Redis or PostgreSQL; Stripe-related keys stay in Redis.
/// </summary>
public class UsageService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    UsageRedisService redis,
    IServiceProvider serviceProvider)
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private UsagePostgresService? Postgres =>
        serviceProvider.GetService(typeof(UsagePostgresService)) as UsagePostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.UsageStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    /// <summary>Used for response headers and client-visible degraded-mode disclosure.</summary>
    public virtual UsageBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.UsageStorage)
            ? "redis"
            : _opts.UsageStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new UsageBackendInfo(effective, configured, degraded, reason);
    }

    public virtual Task<int> GetUsageTodayAsync(string userId) =>
        UsePostgres ? Postgres!.GetUsageTodayAsync(userId) : redis.GetUsageTodayAsync(userId);

    public virtual Task<int> GetUsageTodayStrictAsync(string userId) =>
        UsePostgres ? Postgres!.GetUsageTodayStrictAsync(userId) : redis.GetUsageTodayStrictAsync(userId);

    public virtual Task<int> IncrementUsageAsync(string userId) =>
        UsePostgres ? Postgres!.IncrementUsageAsync(userId) : redis.IncrementUsageAsync(userId);

    public virtual Task<string> GetPlanAsync(string userId) =>
        UsePostgres ? Postgres!.GetPlanAsync(userId) : redis.GetPlanAsync(userId);

    public virtual Task<string> GetPlanStrictAsync(string userId) =>
        UsePostgres ? Postgres!.GetPlanStrictAsync(userId) : redis.GetPlanStrictAsync(userId);

    public virtual Task SetPlanAsync(string userId, string plan) =>
        UsePostgres ? Postgres!.SetPlanAsync(userId, plan) : redis.SetPlanAsync(userId, plan);

    public virtual async Task SetStripeCustomerIdAsync(string userId, string customerId)
    {
        // Always write to Redis (for backward compat and idempotency lookups)
        await redis.SetStripeCustomerIdAsync(userId, customerId);
        // Also persist in Postgres if available
        if (UsePostgres)
            await Postgres!.SetStripeCustomerIdAsync(userId, customerId);
    }

    public virtual async Task<string?> GetUserIdByStripeCustomerIdAsync(string customerId)
    {
        if (UsePostgres)
        {
            var fromPg = await Postgres!.GetUserIdByStripeCustomerIdAsync(customerId);
            if (!string.IsNullOrWhiteSpace(fromPg)) return fromPg;
        }
        return await redis.GetUserIdByStripeCustomerIdAsync(customerId);
    }

    public virtual async Task<string?> GetStripeCustomerIdAsync(string userId)
    {
        if (UsePostgres)
        {
            var fromPg = await Postgres!.GetStripeCustomerIdAsync(userId);
            if (!string.IsNullOrWhiteSpace(fromPg)) return fromPg;
        }
        return await redis.GetStripeCustomerIdAsync(userId);
    }

    public virtual Task<bool> TryAcquireStripeEventAsync(string eventId) =>
        redis.TryAcquireStripeEventAsync(eventId);

    public virtual Task RecordStripeWebhookAuditAsync(StripeWebhookAuditRecord audit) =>
        redis.RecordStripeWebhookAuditAsync(audit);

    public virtual async Task<StripeDebugInfo> GetStripeDebugInfoAsync(string userId)
    {
        var currentPlan = await GetPlanStrictAsync(userId).ConfigureAwait(false);
        var lastEventId = await redis.GetStripeLastEventIdAsync(userId).ConfigureAwait(false);
        var lastEventAt = await redis.GetStripeLastEventAtAsync(userId).ConfigureAwait(false);
        var lastSessionId = await redis.GetStripeLastSessionIdAsync(userId).ConfigureAwait(false);
        return new StripeDebugInfo(userId, currentPlan, lastEventId, lastEventAt, lastSessionId);
    }

    public static int GetDailyLimit(string plan) => plan switch
    {
        "anonymous" => 2,
        "free" => 20,
        "premium" => 200,
        "pro" => int.MaxValue,
        _ => 2,
    };

    public virtual Task<UsageCheckResult> CheckAndIncrementAsync(string userId, bool isAnonymous) =>
        UsePostgres ? Postgres!.CheckAndIncrementAsync(userId, isAnonymous) : redis.CheckAndIncrementAsync(userId, isAnonymous);
}

public sealed class UsageCheckResult
{
    public bool Allowed { get; set; }
    public string? Reason { get; set; }
    public string? Message { get; set; }
    public int UsageToday { get; set; }
    public int DailyLimit { get; set; }
    public string Plan { get; set; } = "free";
}

public sealed record StripeWebhookAuditRecord(
    string EventId,
    string EventType,
    string? SessionId,
    string? UserId,
    string? Plan,
    string ProcessedAt,
    string? CustomerId,
    string? SubscriptionId,
    string Result);

public sealed record StripeDebugInfo(
    string UserId,
    string CurrentPlan,
    string? LastStripeEventId,
    string? LastStripeEventAt,
    string? LastCheckoutSessionId);
