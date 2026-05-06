using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public interface IUsageTrackingService
{
    Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default);
    Task<int> GetDailyCountAsync(string userId, CancellationToken ct = default);
    Task<List<UsageRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
    Task<UsageStats> GetStatsAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<List<ActiveUserUsage>> GetActiveUsersAsync(DateTime from, DateTime to, int limit = 50, CancellationToken ct = default);
}
