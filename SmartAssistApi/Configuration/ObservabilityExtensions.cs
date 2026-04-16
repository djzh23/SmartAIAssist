using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Context;
using SmartAssistApi.Data;
using SmartAssistApi.Health;

namespace SmartAssistApi.Configuration;

public static class ObservabilityExtensions
{
    /// <param name="registerPostgresCheck">When true, adds <see cref="SmartAssistDbContext"/> connectivity check (requires DbContext registration and <c>DatabaseFeatures:PostgresEnabled</c>).</param>
    public static IServiceCollection AddSmartAssistHealthChecks(this IServiceCollection services, bool registerPostgresCheck = false)
    {
        var checks = services.AddHealthChecks()
            .AddCheck<UpstashRedisHealthCheck>("upstash");
        if (registerPostgresCheck)
            checks.AddDbContextCheck<SmartAssistDbContext>("postgres");
        return services;
    }

    /// <summary>
    /// Ensures every response has <c>X-Request-Id</c> and adds <c>RequestId</c> to Serilog <see cref="LogContext"/> for the request.
    /// Run before <c>UseSerilogRequestLogging</c> so completion logs include the id.
    /// </summary>
    public static IApplicationBuilder UseRequestId(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var header = context.Request.Headers["X-Request-Id"].ToString();
            var id = string.IsNullOrWhiteSpace(header) ? Guid.NewGuid().ToString("N") : header.Trim();
            context.Response.Headers["X-Request-Id"] = id;
            using (LogContext.PushProperty("RequestId", id))
                await next();
        });
    }
}
