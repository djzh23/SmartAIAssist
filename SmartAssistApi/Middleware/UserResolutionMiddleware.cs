using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Services;

namespace SmartAssistApi.Middleware;

/// <summary>
/// Resolves the authenticated user on every request.
/// - Extracts userId from the verified JWT (via ClerkAuthService)
/// - Ensures an app_users row exists (single provisioning point)
/// - Populates IAppUserContext for the request scope
///
/// This replaces all scattered EnsureAppUserAsync calls across services.
/// </summary>
public sealed class UserResolutionMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(
        HttpContext context,
        ClerkAuthService authService,
        IAppUserContext appUserContext,
        UsageService usageService,
        IMemoryCache cache,
        ILogger<UserResolutionMiddleware> logger)
    {
        var userCtx = (AppUserContext)appUserContext;
        var (userId, isAnonymous) = authService.ExtractUserId(context.Request);

        userCtx.UserId = userId ?? "";
        userCtx.IsAnonymous = isAnonymous;

        if (isAnonymous || string.IsNullOrWhiteSpace(userId))
        {
            userCtx.Plan = "anonymous";
            await next(context);
            return;
        }

        // Check memory cache first to avoid DB hit on every request
        var cacheKey = $"user_resolved:{userId}";
        if (cache.TryGetValue<ResolvedUser>(cacheKey, out var cached) && cached is not null)
        {
            userCtx.Plan = cached.Plan;
            userCtx.FirstSeenAt = cached.FirstSeenAt;
            await next(context);
            return;
        }

        // Provision user if Postgres is available
        var db = context.RequestServices.GetService<SmartAssistDbContext>();
        if (db is not null)
        {
            var appUser = await db.AppUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.ClerkUserId == userId, context.RequestAborted);

            if (appUser is null)
            {
                // First-time user — create row
                var now = DateTime.UtcNow;
                var newUser = new AppUserEntity
                {
                    ClerkUserId = userId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                try
                {
                    db.AppUsers.Add(newUser);
                    await db.SaveChangesAsync(context.RequestAborted);
                    logger.LogInformation("New user provisioned via middleware. UserId {UserId}", userId);
                }
                catch (DbUpdateException)
                {
                    // Concurrent insert race — row already exists, which is fine
                    db.Entry(newUser).State = EntityState.Detached;
                }

                userCtx.FirstSeenAt = now;
            }
            else
            {
                userCtx.FirstSeenAt = appUser.CreatedAt;
            }
        }

        // Resolve plan
        var plan = await usageService.GetPlanAsync(userId);
        userCtx.Plan = plan;

        // Cache the resolved state
        cache.Set(cacheKey, new ResolvedUser(plan, userCtx.FirstSeenAt), CacheDuration);

        await next(context);
    }

    private sealed record ResolvedUser(string Plan, DateTime FirstSeenAt);
}
