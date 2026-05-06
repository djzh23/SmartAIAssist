using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class RedisConfigurationGuardTests
{
    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

    [Fact]
    public void RedisServices_AllowConstruction_WithoutUpstashConfig()
    {
        var config = EmptyConfig();
        var http = new HttpClient();

        var ex = Record.Exception(() =>
        {
            _ = new UsageRedisService(config, http);
            _ = new TokenTrackingRedisService(config, http, Mock.Of<ILogger<TokenTrackingRedisService>>());
            _ = new UpstashRedisStringStore(config, http);
            _ = new CareerProfileRedisService(config, http, Mock.Of<ILogger<CareerProfileRedisService>>());
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task RedisServices_ThrowClearError_WhenUsedWithoutUpstashConfig()
    {
        var config = EmptyConfig();
        var http = new HttpClient();

        var usage = new UsageRedisService(config, http);
        var tracking = new TokenTrackingRedisService(config, http, Mock.Of<ILogger<TokenTrackingRedisService>>());
        var stringStore = new UpstashRedisStringStore(config, http);
        var profile = new CareerProfileRedisService(config, http, Mock.Of<ILogger<CareerProfileRedisService>>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => usage.GetPlanAsync("user-1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tracking.GetDashboardDataAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => stringStore.StringGetAsync("k1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => profile.GetProfile("user-1"));
    }
}
