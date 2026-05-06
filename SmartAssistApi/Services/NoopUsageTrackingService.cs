using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public sealed class NoopUsageTrackingService : IUsageTrackingService
{
    public Task RecordUsageAsync(UsageRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> GetDailyCountAsync(string userId, CancellationToken ct = default) => Task.FromResult(0);

    public Task<List<UsageRecord>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult(new List<UsageRecord>());

    public Task<UsageStats> GetStatsAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult(new UsageStats());

    public Task<List<ActiveUserUsage>> GetActiveUsersAsync(DateTime from, DateTime to, int limit = 50, CancellationToken ct = default) =>
        Task.FromResult(new List<ActiveUserUsage>());

    public Task<TokenSummary> GetTokenSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult(new TokenSummary());

    public Task<List<TokenByToolRow>> GetTokenByToolAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult(new List<TokenByToolRow>());

    public Task<List<TokenByModelRow>> GetTokenByModelAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult(new List<TokenByModelRow>());

    public Task<List<TokenDailyRow>> GetTokenDailyAsync(int days = 30, CancellationToken ct = default) =>
        Task.FromResult(new List<TokenDailyRow>());

    public Task<RagSummary> GetRagSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
        Task.FromResult(new RagSummary());
}
