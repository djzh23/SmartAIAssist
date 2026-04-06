using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartAssistApi.Services;

public class UsageService(IConfiguration config, HttpClient http)
{
    private readonly string _restUrl = config["Upstash:RestUrl"] ?? throw new InvalidOperationException("Upstash:RestUrl missing");
    private readonly string _restToken = config["Upstash:RestToken"] ?? throw new InvalidOperationException("Upstash:RestToken missing");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static string TodayKey() => DateTime.UtcNow.ToString("yyyy-MM-dd");
    private static string UsageKey(string userId) => $"usage:{userId}:{TodayKey()}";
    private static string PlanKey(string userId) => $"plan:{userId}";

    private async Task<string?> RedisGetAsync(string key)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/get/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"get:{key}");
        var data = DeserializeUpstash(body, $"get:{key}");
        return data?.Result?.ToString();
    }

    private async Task<long> RedisIncrAsync(string key)
    {
        using var req = CreateRequest(HttpMethod.Post, $"/incr/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"incr:{key}");
        var data = DeserializeUpstash(body, $"incr:{key}");
        return ParseLongResult(data?.Result, $"incr:{key}");
    }

    private async Task RedisExpireAsync(string key, int seconds)
    {
        using var req = CreateRequest(HttpMethod.Post, $"/expire/{Uri.EscapeDataString(key)}/{seconds}");
        _ = await SendAsync(req, $"expire:{key}");
    }

    private async Task RedisSetAsync(string key, string value)
    {
        using var req = CreateRequest(HttpMethod.Post, $"/set/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(value)}");
        _ = await SendAsync(req, $"set:{key}");
    }

    private async Task<bool> RedisSetNxAsync(string key, string value)
    {
        using var req = CreateRequest(HttpMethod.Post, $"/setnx/{Uri.EscapeDataString(key)}/{Uri.EscapeDataString(value)}");
        var body = await SendAsync(req, $"setnx:{key}");
        var data = DeserializeUpstash(body, $"setnx:{key}");
        return ParseBoolResult(data?.Result, $"setnx:{key}");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{_restUrl}{path}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        return req;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string operation)
    {
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Upstash operation '{operation}' failed with status {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    private static UpstashResult? DeserializeUpstash(string body, string operation)
    {
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException($"Upstash operation '{operation}' returned empty payload.");

        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException($"Upstash operation '{operation}' failed: {data.Error}");

        return data;
    }

    private static long ParseLongResult(object? result, string operation)
    {
        if (result is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var fromNumber))
            return fromNumber;

        if (result is JsonElement stringElement && stringElement.ValueKind == JsonValueKind.String
            && long.TryParse(stringElement.GetString(), out var fromStringElement))
            return fromStringElement;

        if (result is long n)
            return n;

        if (result is int i)
            return i;

        if (result is string s && long.TryParse(s, out var fromString))
            return fromString;

        throw new InvalidOperationException($"Upstash operation '{operation}' returned non-numeric result.");
    }

    private static bool ParseBoolResult(object? result, string operation)
    {
        if (result is JsonElement element && element.ValueKind == JsonValueKind.True)
            return true;
        if (result is JsonElement elementFalse && elementFalse.ValueKind == JsonValueKind.False)
            return false;
        if (result is JsonElement numberElement && numberElement.ValueKind == JsonValueKind.Number
            && numberElement.TryGetInt64(out var num))
            return num == 1;
        if (result is JsonElement stringElement && stringElement.ValueKind == JsonValueKind.String)
        {
            var value = stringElement.GetString();
            if (bool.TryParse(value, out var b))
                return b;
            if (long.TryParse(value, out var n))
                return n == 1;
        }

        if (result is bool b2)
            return b2;
        if (result is long l)
            return l == 1;
        if (result is int i)
            return i == 1;
        if (result is string s)
        {
            if (bool.TryParse(s, out var b3))
                return b3;
            if (long.TryParse(s, out var n2))
                return n2 == 1;
        }

        throw new InvalidOperationException($"Upstash operation '{operation}' returned non-boolean result.");
    }

    public virtual async Task<int> GetUsageTodayAsync(string userId)
    {
        var val = await RedisGetAsync(UsageKey(userId));
        if (string.IsNullOrWhiteSpace(val))
            return 0;

        if (int.TryParse(val, out var n))
            return n;

        throw new InvalidOperationException($"Usage value for '{userId}' is invalid: '{val}'.");
    }

    public virtual Task<int> GetUsageTodayStrictAsync(string userId) => GetUsageTodayAsync(userId);

    public virtual async Task<int> IncrementUsageAsync(string userId)
    {
        var newCount = (int)await RedisIncrAsync(UsageKey(userId));
        if (newCount == 1)
            await RedisExpireAsync(UsageKey(userId), 172_800); // 48 h

        return newCount;
    }

    public virtual async Task<string> GetPlanAsync(string userId)
    {
        var val = await RedisGetAsync(PlanKey(userId));
        return string.IsNullOrWhiteSpace(val) ? "free" : val;
    }

    public virtual Task<string> GetPlanStrictAsync(string userId) => GetPlanAsync(userId);

    public virtual Task SetPlanAsync(string userId, string plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            throw new ArgumentException("Plan must not be empty.", nameof(plan));
        return RedisSetAsync(PlanKey(userId), plan);
    }

    public virtual async Task SetStripeCustomerIdAsync(string userId, string customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("Stripe customer id must not be empty.", nameof(customerId));

        await RedisSetAsync($"stripe_customer:{userId}", customerId);
        await RedisSetAsync($"user_by_stripe:{customerId}", userId);
    }

    public virtual Task<string?> GetUserIdByStripeCustomerIdAsync(string customerId) =>
        RedisGetAsync($"user_by_stripe:{customerId}");

    public virtual Task<string?> GetStripeCustomerIdAsync(string userId) =>
        RedisGetAsync($"stripe_customer:{userId}");

    public virtual Task<bool> TryAcquireStripeEventAsync(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("Stripe event id must not be empty.", nameof(eventId));

        return RedisSetNxAsync($"stripe_event_processed:{eventId}", DateTimeOffset.UtcNow.ToString("O"));
    }

    public virtual async Task RecordStripeWebhookAuditAsync(StripeWebhookAuditRecord audit)
    {
        if (string.IsNullOrWhiteSpace(audit.EventId))
            throw new ArgumentException("Stripe event id is required for audit.");
        if (string.IsNullOrWhiteSpace(audit.ProcessedAt))
            throw new ArgumentException("ProcessedAt is required for audit.");

        var auditJson = JsonSerializer.Serialize(audit, JsonOpts);
        await RedisSetAsync($"stripe_event_audit:{audit.EventId}", auditJson);

        if (!string.IsNullOrWhiteSpace(audit.UserId))
        {
            await RedisSetAsync($"stripe_last_event:{audit.UserId}", audit.EventId);
            await RedisSetAsync($"stripe_last_event_at:{audit.UserId}", audit.ProcessedAt);

            if (!string.IsNullOrWhiteSpace(audit.SessionId))
                await RedisSetAsync($"stripe_last_session:{audit.UserId}", audit.SessionId);
        }
    }

    public virtual async Task<StripeDebugInfo> GetStripeDebugInfoAsync(string userId)
    {
        var currentPlan = await GetPlanStrictAsync(userId);
        var lastEventId = await RedisGetAsync($"stripe_last_event:{userId}");
        var lastEventAt = await RedisGetAsync($"stripe_last_event_at:{userId}");
        var lastSessionId = await RedisGetAsync($"stripe_last_session:{userId}");

        return new StripeDebugInfo(
            userId,
            currentPlan,
            lastEventId,
            lastEventAt,
            lastSessionId);
    }

    public static int GetDailyLimit(string plan) => plan switch
    {
        "anonymous" => 2,
        "free" => 20,
        "premium" => 200,
        "pro" => int.MaxValue,
        _ => 2,
    };

    public virtual async Task<UsageCheckResult> CheckAndIncrementAsync(string userId, bool isAnonymous)
    {
        if (isAnonymous)
        {
            var anonKey = $"anon:{userId}";
            var anonUsage = await GetUsageTodayAsync(anonKey);
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

            var newUsage = await IncrementUsageAsync(anonKey);
            return new UsageCheckResult { Allowed = true, UsageToday = newUsage, DailyLimit = anonLimit, Plan = "anonymous" };
        }

        var plan = await GetPlanAsync(userId);
        var limit = GetDailyLimit(plan);
        var usage = await GetUsageTodayAsync(userId);

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

        var incremented = await IncrementUsageAsync(userId);
        return new UsageCheckResult { Allowed = true, UsageToday = incremented, DailyLimit = limit, Plan = plan };
    }

    private sealed record UpstashResult(
        [property: JsonPropertyName("result")] object? Result,
        [property: JsonPropertyName("error")] string? Error);
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
