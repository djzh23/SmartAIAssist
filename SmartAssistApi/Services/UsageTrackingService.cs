using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public sealed class UsageTrackingService(SmartAssistDbContext db) : IUsageTrackingService
{
    public async Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default)
    {
        var entity = new UsageRecordEntity
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            UserId = record.UserId.Trim(),
            ToolType = string.IsNullOrWhiteSpace(record.ToolType) ? "general" : record.ToolType.Trim().ToLowerInvariant(),
            SessionId = record.SessionId.Trim(),
            CreatedAt = record.CreatedAt == default ? DateTime.UtcNow : record.CreatedAt,
            InputTokens = Math.Max(0, record.InputTokens),
            OutputTokens = Math.Max(0, record.OutputTokens),
            CacheCreationTokens = Math.Max(0, record.CacheCreationTokens),
            CacheReadTokens = Math.Max(0, record.CacheReadTokens),
            Model = string.IsNullOrWhiteSpace(record.Model) ? null : record.Model.Trim(),
            ResponseTimeMs = record.ResponseTimeMs,
            EstimatedCostUsd = record.EstimatedCostUsd,
        };

        db.UsageRecords.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task<int> GetDailyCountAsync(string userId, CancellationToken ct = default)
    {
        var dayStart = DateTime.UtcNow.Date;
        var dayEnd = dayStart.AddDays(1);
        return db.UsageRecords.AsNoTracking()
            .CountAsync(
                x => x.UserId == userId
                     && x.CreatedAt >= dayStart
                     && x.CreatedAt < dayEnd,
                ct);
    }

    public Task<List<UsageRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        var safeLimit = Math.Clamp(limit, 1, 200);
        return db.UsageRecords.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(safeLimit)
            .Select(x => new UsageRecord
            {
                Id = x.Id,
                UserId = x.UserId,
                ToolType = x.ToolType,
                SessionId = x.SessionId,
                CreatedAt = x.CreatedAt,
                InputTokens = x.InputTokens,
                OutputTokens = x.OutputTokens,
                CacheCreationTokens = x.CacheCreationTokens,
                CacheReadTokens = x.CacheReadTokens,
                Model = x.Model,
                ResponseTimeMs = x.ResponseTimeMs,
                EstimatedCostUsd = x.EstimatedCostUsd,
            })
            .ToListAsync(ct);
    }

    public async Task<UsageStats> GetStatsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var start = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var endExclusive = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        if (endExclusive <= start)
            endExclusive = start.AddDays(1);

        var todayStart = DateTime.UtcNow.Date;
        var weekStart = todayStart.AddDays(-6);
        var monthStart = new DateTime(todayStart.Year, todayStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var recentStart = DateTime.UtcNow.AddHours(-24);

        var turnsToday = await db.UsageRecords.AsNoTracking()
            .CountAsync(x => x.CreatedAt >= todayStart && x.CreatedAt < todayStart.AddDays(1), ct)
            .ConfigureAwait(false);
        var turnsThisWeek = await db.UsageRecords.AsNoTracking()
            .CountAsync(x => x.CreatedAt >= weekStart && x.CreatedAt < todayStart.AddDays(1), ct)
            .ConfigureAwait(false);
        var turnsThisMonth = await db.UsageRecords.AsNoTracking()
            .CountAsync(x => x.CreatedAt >= monthStart && x.CreatedAt < todayStart.AddDays(1), ct)
            .ConfigureAwait(false);
        var activeUsers7d = await db.UsageRecords.AsNoTracking()
            .Where(x => x.CreatedAt >= weekStart && x.CreatedAt < todayStart.AddDays(1))
            .Select(x => x.UserId)
            .Distinct()
            .CountAsync(ct)
            .ConfigureAwait(false);
        var recentRecords = await db.UsageRecords.AsNoTracking()
            .CountAsync(x => x.CreatedAt >= recentStart, ct)
            .ConfigureAwait(false);

        return new UsageStats
        {
            TurnsToday = turnsToday,
            TurnsThisWeek = turnsThisWeek,
            TurnsThisMonth = turnsThisMonth,
            ActiveUsers7d = activeUsers7d,
            RecentRecordsCount = recentRecords,
        };
    }

    public Task<List<ActiveUserUsage>> GetActiveUsersAsync(DateTime from, DateTime to, int limit = 50, CancellationToken ct = default)
    {
        var start = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var endExclusive = DateTime.SpecifyKind(to, DateTimeKind.Utc);
        if (endExclusive <= start)
            endExclusive = start.AddDays(1);

        var safeLimit = Math.Clamp(limit, 1, 200);
        return db.UsageRecords.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < endExclusive)
            .GroupBy(x => x.UserId)
            .Select(g => new ActiveUserUsage
            {
                UserId = g.Key,
                TurnsCount = g.Count(),
                LastSeenAt = g.Max(x => x.CreatedAt),
                TotalEstimatedCostUsd = g.Sum(x => x.EstimatedCostUsd ?? 0m),
            })
            .OrderByDescending(x => x.TurnsCount)
            .ThenByDescending(x => x.LastSeenAt)
            .Take(safeLimit)
            .ToListAsync(ct);
    }
}
