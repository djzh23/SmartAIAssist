using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using SmartAssistApi.Data;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class UsageServiceTests
{
    private static UsageService CreateRedisUsageService(IConfiguration config, HttpClient http)
    {
        var opt = new Mock<IOptionsSnapshot<DatabaseFeatureOptions>>();
        opt.Setup(o => o.Value).Returns(new DatabaseFeatureOptions { PostgresEnabled = false, UsageStorage = "redis", TokenUsageStorage = "redis" });
        return new UsageService(opt.Object, new UsageRedisService(config, http), new ServiceCollection().BuildServiceProvider());
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upstash:RestUrl"]   = "https://fake-upstash.upstash.io",
                ["Upstash:RestToken"] = "fake-token",
            })
            .Build();

    private static HttpClient BuildHttpClient(params (string pattern, object body)[] responses)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        var queue = new Queue<object>(responses.Select(r => r.body));

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var body = queue.Count > 0 ? queue.Dequeue() : new { result = (object?)null };
                var json = JsonSerializer.Serialize(body);
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content    = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                };
            });

        return new HttpClient(handlerMock.Object);
    }

    // ── GetUsageTodayAsync ────────────────────────────────

    [Fact]
    public async Task GetUsageTodayAsync_NewUser_ReturnsZero()
    {
        var http    = BuildHttpClient((string.Empty, new { result = (object?)null }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var usage = await service.GetUsageTodayAsync("user_new");

        Assert.Equal(0, usage);
    }

    [Fact]
    public async Task GetUsageTodayAsync_ExistingUser_ReturnsStoredCount()
    {
        var http    = BuildHttpClient((string.Empty, new { result = "5" }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var usage = await service.GetUsageTodayAsync("user_existing");

        Assert.Equal(5, usage);
    }

    // ── IncrementUsageAsync ───────────────────────────────

    [Fact]
    public async Task IncrementUsageAsync_FirstCall_ReturnsOne()
    {
        // INCR returns 1 (new key), EXPIRE returns 1
        var http    = BuildHttpClient(
            (string.Empty, new { result = 1L }),
            (string.Empty, new { result = 1L }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var count = await service.IncrementUsageAsync("user_new");

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task IncrementUsageAsync_SubsequentCall_ReturnsIncrementedCount()
    {
        // INCR returns 3 (existing key, no expire)
        var http    = BuildHttpClient((string.Empty, new { result = 3L }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var count = await service.IncrementUsageAsync("user_existing");

        Assert.Equal(3, count);
    }

    // ── GetDailyLimit ─────────────────────────────────────

    [Theory]
    [InlineData("anonymous", 2)]
    [InlineData("free",      20)]
    [InlineData("premium",   200)]
    public void GetDailyLimit_KnownPlan_ReturnsCorrectLimit(string plan, int expected)
    {
        Assert.Equal(expected, UsageService.GetDailyLimit(plan));
    }

    [Fact]
    public void GetDailyLimit_ProPlan_ReturnsMaxValue()
    {
        Assert.Equal(int.MaxValue, UsageService.GetDailyLimit("pro"));
    }

    [Fact]
    public void GetDailyLimit_UnknownPlan_ReturnsTwoAsDefault()
    {
        Assert.Equal(2, UsageService.GetDailyLimit("enterprise"));
    }

    // ── CheckAndIncrementAsync – anonymous ───────────────

    [Fact]
    public async Task CheckAndIncrementAsync_AnonymousAtLimit_ReturnsNotAllowed()
    {
        // GET returns 2 (already at limit of 2)
        var http    = BuildHttpClient((string.Empty, new { result = "2" }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var result = await service.CheckAndIncrementAsync("ip:1.2.3.4", isAnonymous: true);

        Assert.False(result.Allowed);
        Assert.Equal("anonymous_limit", result.Reason);
        Assert.Equal(2, result.UsageToday);
        Assert.Equal(2, result.DailyLimit);
        Assert.Equal("anonymous", result.Plan);
    }

    [Fact]
    public async Task CheckAndIncrementAsync_AnonymousBelowLimit_ReturnsAllowed()
    {
        // GET returns 0, then INCR returns 1, EXPIRE returns 1
        var http    = BuildHttpClient(
            (string.Empty, new { result = "0" }),
            (string.Empty, new { result = 1L }),
            (string.Empty, new { result = 1L }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var result = await service.CheckAndIncrementAsync("ip:1.2.3.4", isAnonymous: true);

        Assert.True(result.Allowed);
        Assert.Equal(1, result.UsageToday);
        Assert.Equal(2, result.DailyLimit);
        Assert.Equal("anonymous", result.Plan);
    }

    // ── CheckAndIncrementAsync – signed-in ───────────────

    [Fact]
    public async Task CheckAndIncrementAsync_FreeUserAtLimit_ReturnsNotAllowed()
    {
        // GetPlan → "free", GetUsage → 20 (at limit)
        var http = BuildHttpClient(
            (string.Empty, new { result = "free" }),
            (string.Empty, new { result = "20" }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var result = await service.CheckAndIncrementAsync("user_abc", isAnonymous: false);

        Assert.False(result.Allowed);
        Assert.Equal("free_limit", result.Reason);
        Assert.Equal(20, result.UsageToday);
        Assert.Equal(20, result.DailyLimit);
        Assert.Equal("free", result.Plan);
    }

    [Fact]
    public async Task CheckAndIncrementAsync_FreeUserBelowLimit_ReturnsAllowed()
    {
        // GetPlan → "free", GetUsage → 5, INCR → 6, EXPIRE → 1
        var http = BuildHttpClient(
            (string.Empty, new { result = "free" }),
            (string.Empty, new { result = "5" }),
            (string.Empty, new { result = 6L }),
            (string.Empty, new { result = 1L }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var result = await service.CheckAndIncrementAsync("user_abc", isAnonymous: false);

        Assert.True(result.Allowed);
        Assert.Equal(6, result.UsageToday);
        Assert.Equal(20, result.DailyLimit);
        Assert.Equal("free", result.Plan);
    }

    [Fact]
    public async Task CheckAndIncrementAsync_ProUser_IsNeverLimited()
    {
        // GetPlan → "pro", GetUsage → 999, INCR → 1000
        var http = BuildHttpClient(
            (string.Empty, new { result = "pro" }),
            (string.Empty, new { result = "999" }),
            (string.Empty, new { result = 1000L }));
        var service = CreateRedisUsageService(BuildConfig(), http);

        var result = await service.CheckAndIncrementAsync("user_pro", isAnonymous: false);

        Assert.True(result.Allowed);
        Assert.Equal("pro", result.Plan);
        Assert.Equal(int.MaxValue, result.DailyLimit);
    }
}
