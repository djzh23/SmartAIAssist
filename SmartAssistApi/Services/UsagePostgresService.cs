using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;

namespace SmartAssistApi.Services;

/// <summary>Daily usage counts and plan in PostgreSQL (replaces usage:{userId}:{date} and plan:{userId}).</summary>
public sealed class UsagePostgresService(SmartAssistDbContext db)
{
    private static DateOnly TodayUtc() => DateOnly.FromDateTime(DateTime.UtcNow.Date);

    public async Task<int> GetUsageTodayAsync(string userId, CancellationToken cancellationToken = default)
    {
        var today = TodayUtc();
        var row = await db.UserUsageDaily.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId && x.UsageDate == today, cancellationToken)
            .ConfigureAwait(false);
        return row?.UsageCount ?? 0;
    }

    public Task<int> GetUsageTodayStrictAsync(string userId, CancellationToken cancellationToken = default) =>
        GetUsageTodayAsync(userId, cancellationToken);

    public async Task<int> IncrementUsageAsync(string userId, CancellationToken cancellationToken = default)
    {
        var today = TodayUtc();
        var row = await db.UserUsageDaily
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId && x.UsageDate == today, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            db.UserUsageDaily.Add(new UserUsageDailyEntity
            {
                ClerkUserId = userId,
                UsageDate = today,
                UsageCount = 1,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.UsageCount++;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetUsageTodayAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetPlanAsync(string userId, CancellationToken cancellationToken = default)
    {
        var row = await db.UserPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(row?.Plan) ? "free" : row!.Plan;
    }

    public Task<string> GetPlanStrictAsync(string userId, CancellationToken cancellationToken = default) =>
        GetPlanAsync(userId, cancellationToken);

    public async Task SetPlanAsync(string userId, string plan, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plan))
            throw new ArgumentException("Plan must not be empty.", nameof(plan));

        var row = await db.UserPlans.FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            db.UserPlans.Add(new UserPlanEntity { ClerkUserId = userId, Plan = plan.Trim(), UpdatedAt = now });
        }
        else
        {
            row.Plan = plan.Trim();
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageCheckResult> CheckAndIncrementAsync(string userId, bool isAnonymous, CancellationToken cancellationToken = default)
    {
        if (isAnonymous)
        {
            var anonKey = $"anon:{userId}";
            var anonUsage = await GetUsageTodayAsync(anonKey, cancellationToken).ConfigureAwait(false);
            const int anonLimit = 2;

            if (anonUsage >= anonLimit)
                return new UsageCheckResult
                {
                    Allowed = false,
                    Reason = "anonymous_limit",
                    Message = "Sign in to get 20 free responses per day",
                    UsageToday = anonUsage,
                    DailyLimit = anonLimit,
                    Plan = "anonymous",
                };

            var newUsage = await IncrementUsageAsync(anonKey, cancellationToken).ConfigureAwait(false);
            return new UsageCheckResult { Allowed = true, UsageToday = newUsage, DailyLimit = anonLimit, Plan = "anonymous" };
        }

        var plan = await GetPlanAsync(userId, cancellationToken).ConfigureAwait(false);
        var limit = UsageService.GetDailyLimit(plan);
        var usage = await GetUsageTodayAsync(userId, cancellationToken).ConfigureAwait(false);

        if (limit != int.MaxValue && usage >= limit)
            return new UsageCheckResult
            {
                Allowed = false,
                Reason = plan == "free" ? "free_limit" : "plan_limit",
                Message = plan == "free" ? "Upgrade to Premium for 200 responses/day" : "Daily limit reached",
                UsageToday = usage,
                DailyLimit = limit,
                Plan = plan,
            };

        var incremented = await IncrementUsageAsync(userId, cancellationToken).ConfigureAwait(false);
        return new UsageCheckResult { Allowed = true, UsageToday = incremented, DailyLimit = limit, Plan = plan };
    }
}
