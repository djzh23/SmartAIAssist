using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAssistApi.Health;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Health;

public sealed class UpstashRedisHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_When_store_get_succeeds()
    {
        var mockStore = new Mock<IRedisStringStore>(MockBehavior.Strict);
        mockStore
            .Setup(s => s.StringGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var services = new ServiceCollection();
        services.AddScoped(_ => mockStore.Object);
        await using var provider = services.BuildServiceProvider();

        var sut = new UpstashRedisHealthCheck(provider, NullLogger<UpstashRedisHealthCheck>.Instance);
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_When_store_throws()
    {
        var mockStore = new Mock<IRedisStringStore>(MockBehavior.Strict);
        mockStore
            .Setup(s => s.StringGetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Upstash:RestUrl is missing."));

        var services = new ServiceCollection();
        services.AddScoped(_ => mockStore.Object);
        await using var provider = services.BuildServiceProvider();

        var sut = new UpstashRedisHealthCheck(provider, NullLogger<UpstashRedisHealthCheck>.Instance);
        var result = await sut.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }
}
