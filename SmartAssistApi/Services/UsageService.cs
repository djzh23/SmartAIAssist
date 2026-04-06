using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAssistApi.Services;

public class UsageService(IConfiguration config, HttpClient http)
{
    private readonly string _restUrl  = config["Upstash:RestUrl"]   ?? throw new InvalidOperationException("Upstash:RestUrl missing");
    private readonly string _restToken = config["Upstash:RestToken"] ?? throw new InvalidOperationException("Upstash:RestToken missing");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // ── Key helpers ───────────────────────────────────────
    private static string TodayKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string UsageKey(string userId) => $"usage:{userId}:{TodayKey()}";
    private static string PlanKey(string userId)  => $"plan:{userId}";

    // ── Upstash REST helpers ──────────────────────────────
    private async Task<string?> RedisGetAsync(string key)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_restUrl}/get/{Uri.EscapeDataString(key)}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        var res  = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts);
        return data?.Result?.ToString();
    }

    private async Task<long> RedisIncrAsync(string key)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_restUrl}/incr/{Uri.EscapeDataString(key)}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        var res  = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts);
        return data?.Result is JsonElement el && el.TryGetInt64(out var n) ? n : 1;
    }

    private async Task RedisExpireAsync(string key, int seconds)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_restUrl}/expire/{Uri.EscapeDataString(key)}/{seconds}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        await http.SendAsync(req);
    }

    private async Task RedisSetAsync(string key, string value)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_restUrl}/set/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(value)}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        await http.SendAsync(req);
    }

    // ── Public API ────────────────────────────────────────
    public virtual async Task<int> GetUsageTodayAsync(string userId)
    {
        try
        {
            var val = await RedisGetAsync(UsageKey(userId));
            return int.TryParse(val, out var n) ? n : 0;
        }
        catch { return 0; }
    }

    public async Task<int> IncrementUsageAsync(string userId)
    {
        try
        {
            var newCount = (int)await RedisIncrAsync(UsageKey(userId));
            if (newCount == 1)
                await RedisExpireAsync(UsageKey(userId), 172_800); // 48 h
            return newCount;
        }
        catch { return 0; }
    }

    public async Task<string> GetPlanAsync(string userId)
    {
        try
        {
            var val = await RedisGetAsync(PlanKey(userId));
            return val ?? "free";
        }
        catch { return "free"; }
    }

    public async Task SetPlanAsync(string userId, string plan)
    {
        try { await RedisSetAsync(PlanKey(userId), plan); }
        catch { /* best-effort */ }
    }

    public async Task SetStripeCustomerIdAsync(string userId, string customerId)
    {
        try
        {
            await RedisSetAsync($"stripe_customer:{userId}", customerId);
            await RedisSetAsync($"user_by_stripe:{customerId}", userId);
        }
        catch { }
    }

    public async Task<string?> GetUserIdByStripeCustomerIdAsync(string customerId)
    {
        try { return await RedisGetAsync($"user_by_stripe:{customerId}"); }
        catch { return null; }
    }

    public async Task<string?> GetStripeCustomerIdAsync(string userId)
    {
        try { return await RedisGetAsync($"stripe_customer:{userId}"); }
        catch { return null; }
    }

    public static int GetDailyLimit(string plan) => plan switch
    {
        "anonymous" => 2,
        "free"      => 20,
        "premium"   => 200,
        "pro"       => int.MaxValue,
        _           => 2,
    };

    public virtual async Task<UsageCheckResult> CheckAndIncrementAsync(string userId, bool isAnonymous)
    {
        if (isAnonymous)
        {
            var anonKey   = $"anon:{userId}";
            var anonUsage = await GetUsageTodayAsync(anonKey);
            const int anonLimit = 2;

            if (anonUsage >= anonLimit)
                return new UsageCheckResult
                {
                    Allowed    = false,
                    Reason     = "anonymous_limit",
                    Message    = "Sign in to get 20 free responses per day",
                    UsageToday = anonUsage,
                    DailyLimit = anonLimit,
                    Plan       = "anonymous",
                };

            var newUsage = await IncrementUsageAsync(anonKey);
            return new UsageCheckResult { Allowed = true, UsageToday = newUsage, DailyLimit = anonLimit, Plan = "anonymous" };
        }

        var plan  = await GetPlanAsync(userId);
        var limit = GetDailyLimit(plan);
        var usage = await GetUsageTodayAsync(userId);

        if (limit != int.MaxValue && usage >= limit)
            return new UsageCheckResult
            {
                Allowed    = false,
                Reason     = plan == "free" ? "free_limit" : "plan_limit",
                Message    = plan == "free" ? "Upgrade to Premium for 200 responses/day" : "Daily limit reached",
                UsageToday = usage,
                DailyLimit = limit,
                Plan       = plan,
            };

        var incremented = await IncrementUsageAsync(userId);
        return new UsageCheckResult { Allowed = true, UsageToday = incremented, DailyLimit = limit, Plan = plan };
    }

    // ── Upstash response model ────────────────────────────
    private sealed record UpstashResult([property: JsonPropertyName("result")] object? Result);
}

public sealed class UsageCheckResult
{
    public bool    Allowed    { get; set; }
    public string? Reason     { get; set; }
    public string? Message    { get; set; }
    public int     UsageToday { get; set; }
    public int     DailyLimit { get; set; }
    public string  Plan       { get; set; } = "free";
}
