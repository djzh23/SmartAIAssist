using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartAssistApi.Services;

namespace SmartAssistApi.Health;

/// <summary>Verifies Upstash REST (same path as <see cref="IRedisStringStore"/>) with a lightweight GET.</summary>
public sealed class UpstashRedisHealthCheck(IServiceProvider services, ILogger<UpstashRedisHealthCheck> logger)
    : IHealthCheck
{
    private const string ProbeKey = "__smartassist_api_health__";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IRedisStringStore>();
            _ = await store.StringGetAsync(ProbeKey, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "UpstashRedis health check failed");
            return HealthCheckResult.Unhealthy("Upstash REST unreachable or misconfigured", ex);
        }
    }
}
