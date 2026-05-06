using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/admin")]
[EnableRateLimiting("admin")]
public class AdminController(
    TokenTrackingService tracking,
    IUsageTrackingService usageTracking,
    IAppUserContext userContext,
    IConfiguration configuration,
    ILogger<AdminController> logger) : ControllerBase
{
    /// <summary>
    /// Copies <c>job_apps:{userId}</c> from Redis into Postgres with preserved timestamps.
    /// Requires <c>003_job_applications.sql</c> and a valid Supabase connection. Test on staging first.
    /// </summary>
    [HttpPost("migrations/backfill-job-applications/{userId}")]
    public async Task<IActionResult> BackfillJobApplications(
        string userId,
        [FromServices] ApplicationsRedisService redisApplications,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId_required" });

        var pg = services.GetService(typeof(ApplicationsPostgresService)) as ApplicationsPostgresService;
        if (pg is null)
        {
            return StatusCode(
                503,
                new
                {
                    error = "postgres_not_configured",
                    message = "No Supabase/EF connection. Set DATABASE_URL or ConnectionStrings:Supabase.",
                });
        }

        try
        {
            var docs = await redisApplications.ListAsync(userId, cancellationToken).ConfigureAwait(false);
            await pg.ImportDocumentsPreservingTimestampsAsync(userId, docs, cancellationToken).ConfigureAwait(false);
            return Ok(new { success = true, count = docs.Count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill job applications failed for {UserId}", userId);
            return StatusCode(500, new { error = "backfill_failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Copies <c>profile:{userId}</c>, <c>profile:{userId}:cv_raw</c>, and <c>profile_version:{userId}</c> from Redis into Postgres.
    /// Requires <c>004_career_profiles.sql</c> and a valid Supabase connection. Test on staging first.
    /// </summary>
    [HttpPost("migrations/backfill-career-profile/{userId}")]
    public async Task<IActionResult> BackfillCareerProfile(
        string userId,
        [FromServices] CareerProfileRedisService redisProfile,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId_required" });

        var pg = services.GetService(typeof(CareerProfilePostgresService)) as CareerProfilePostgresService;
        if (pg is null)
        {
            return StatusCode(
                503,
                new
                {
                    error = "postgres_not_configured",
                    message = "No Supabase/EF connection. Set DATABASE_URL or ConnectionStrings:Supabase.",
                });
        }

        try
        {
            var profile = await redisProfile.GetProfile(userId).ConfigureAwait(false);
            if (profile is null)
                return Ok(new { success = true, migrated = false });

            var cvRaw = await redisProfile.GetCvRawAsync(userId).ConfigureAwait(false);
            var versionRaw = await redisProfile.GetProfileVersionRawAsync(userId).ConfigureAwait(false);
            long? cacheVersion = long.TryParse(versionRaw, out var v) ? v : null;

            await pg.ImportFromRedisAsync(userId, profile, cvRaw, cacheVersion, cancellationToken).ConfigureAwait(false);
            return Ok(new { success = true, migrated = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill career profile failed for {UserId}", userId);
            return StatusCode(500, new { error = "backfill_failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Copies <c>chat_sessions_index:{userId}</c> and <c>chat_transcript:{userId}:{sessionId}</c> from Redis into Postgres.
    /// Requires <c>005_chat_sessions.sql</c> and a valid Supabase connection. Test on staging first.
    /// </summary>
    [HttpPost("migrations/backfill-chat-sessions/{userId}")]
    public async Task<IActionResult> BackfillChatSessions(
        string userId,
        [FromServices] ChatSessionRedisService redisSessions,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId_required" });

        var pg = services.GetService(typeof(ChatSessionPostgresService)) as ChatSessionPostgresService;
        if (pg is null)
        {
            return StatusCode(
                503,
                new
                {
                    error = "postgres_not_configured",
                    message = "No Supabase/EF connection. Set DATABASE_URL or ConnectionStrings:Supabase.",
                });
        }

        try
        {
            await pg.ImportFromRedisAsync(userId, redisSessions, cancellationToken).ConfigureAwait(false);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill chat sessions failed for {UserId}", userId);
            return StatusCode(500, new { error = "backfill_failed", message = ex.Message });
        }
    }

    /// <summary>
    /// Copies <c>learning:{userId}</c> from Redis into Postgres.
    /// Requires <c>006_learning_memory.sql</c> and a valid Supabase connection. Test on staging first.
    /// </summary>
    [HttpPost("migrations/backfill-learning-memory/{userId}")]
    public async Task<IActionResult> BackfillLearningMemory(
        string userId,
        [FromServices] LearningMemoryRedisService redisLearning,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId_required" });

        var pg = services.GetService(typeof(LearningMemoryPostgresService)) as LearningMemoryPostgresService;
        if (pg is null)
        {
            return StatusCode(
                503,
                new
                {
                    error = "postgres_not_configured",
                    message = "No Supabase/EF connection. Set DATABASE_URL or ConnectionStrings:Supabase.",
                });
        }

        try
        {
            var json = await redisLearning.GetRawJsonAsync(userId, cancellationToken).ConfigureAwait(false);
            await pg.ImportFromRedisJsonAsync(userId, json, cancellationToken).ConfigureAwait(false);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backfill learning memory failed for {UserId}", userId);
            return StatusCode(500, new { error = "backfill_failed", message = ex.Message });
        }
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        try
        {
            var data = await tracking.GetDashboardDataAsync(cancellationToken).ConfigureAwait(false);
            return Ok(data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin dashboard read failed");
            return StatusCode(503, new { error = "dashboard_unavailable", message = "Could not load dashboard data." });
        }
    }

    [HttpGet("users/{userId}/usage")]
    public async Task<IActionResult> GetUserUsage(
        string userId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        var startDate = from ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var endDate = to ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

        try
        {
            var data = await tracking.GetUserUsageAsync(userId, startDate, endDate, cancellationToken).ConfigureAwait(false);
            return Ok(data);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_args", message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin user usage read failed for {UserId}", userId);
            return StatusCode(503, new { error = "usage_unavailable", message = "Could not load usage data." });
        }
    }

    [HttpGet("top-users")]
    public async Task<IActionResult> GetTopUsers(
        [FromQuery] string? date,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        try
        {
            if (!string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to))
            {
                var data = await tracking.GetTopUsersForDateRangeQueryAsync(from.Trim(), to.Trim(), limit, cancellationToken)
                    .ConfigureAwait(false);
                return Ok(data);
            }

            var d = date ?? DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var singleDay = await tracking.GetTopUsersAsync(d, limit, cancellationToken).ConfigureAwait(false);
            return Ok(singleDay);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_args", message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin top users read failed");
            return StatusCode(503, new { error = "top_users_unavailable", message = "Could not load top users." });
        }
    }

    [HttpGet("daily-stats")]
    public async Task<IActionResult> GetDailyStats([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        try
        {
            var data = await tracking.GetDailyStatsAsync(days, cancellationToken).ConfigureAwait(false);
            return Ok(data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin daily stats read failed");
            return StatusCode(503, new { error = "daily_stats_unavailable", message = "Could not load daily stats." });
        }
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        try
        {
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var stats = await usageTracking.GetStatsAsync(monthStart, now.AddDays(1), cancellationToken).ConfigureAwait(false);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin stats read failed");
            return StatusCode(503, new { error = "stats_unavailable", message = "Could not load stats data." });
        }
    }

    [HttpGet("usage/recent")]
    public async Task<IActionResult> GetRecentUsage([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        try
        {
            var rows = await usageTracking.GetRecentAsync(limit, cancellationToken).ConfigureAwait(false);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin recent usage read failed");
            return StatusCode(503, new { error = "usage_recent_unavailable", message = "Could not load recent usage." });
        }
    }

    [HttpGet("users/active")]
    public async Task<IActionResult> GetActiveUsers(
        [FromQuery] int days = 7,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        try
        {
            var safeDays = Math.Clamp(days, 1, 30);
            var now = DateTime.UtcNow;
            var from = now.Date.AddDays(-(safeDays - 1));
            var rows = await usageTracking.GetActiveUsersAsync(from, now.AddDays(1), limit, cancellationToken).ConfigureAwait(false);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin active users read failed");
            return StatusCode(503, new { error = "active_users_unavailable", message = "Could not load active users." });
        }
    }

    [HttpGet("tokens/summary")]
    public async Task<IActionResult> GetTokensSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (!TryParseRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error = "invalid_args", message = error });

        var data = await usageTracking.GetTokenSummaryAsync(start, end, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    [HttpGet("tokens/by-tool")]
    public async Task<IActionResult> GetTokensByTool(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (!TryParseRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error = "invalid_args", message = error });

        var data = await usageTracking.GetTokenByToolAsync(start, end, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    [HttpGet("tokens/by-model")]
    public async Task<IActionResult> GetTokensByModel(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (!TryParseRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error = "invalid_args", message = error });

        var data = await usageTracking.GetTokenByModelAsync(start, end, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    [HttpGet("tokens/daily")]
    public async Task<IActionResult> GetTokensDaily([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });
        var data = await usageTracking.GetTokenDailyAsync(days, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    [HttpGet("tokens/recent")]
    public async Task<IActionResult> GetTokensRecent([FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });
        var data = await usageTracking.GetRecentAsync(limit, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    [HttpGet("rag/summary")]
    public async Task<IActionResult> GetRagSummary(
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return StatusCode(403, new { error = "forbidden" });

        if (!TryParseRange(from, to, out var start, out var end, out var error))
            return BadRequest(new { error = "invalid_args", message = error });

        var data = await usageTracking.GetRagSummaryAsync(start, end, cancellationToken).ConfigureAwait(false);
        return Ok(data);
    }

    private static bool TryParseRange(
        string? from,
        string? to,
        out DateTime start,
        out DateTime endExclusive,
        out string? error)
    {
        var fromRaw = string.IsNullOrWhiteSpace(from) ? DateTime.UtcNow.Date.AddDays(-6).ToString("yyyy-MM-dd") : from.Trim();
        var toRaw = string.IsNullOrWhiteSpace(to) ? DateTime.UtcNow.Date.ToString("yyyy-MM-dd") : to.Trim();
        if (!DateTime.TryParseExact(fromRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fromDt)
            || !DateTime.TryParseExact(toRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var toDt))
        {
            start = default;
            endExclusive = default;
            error = "Use yyyy-MM-dd format for from/to.";
            return false;
        }

        start = DateTime.SpecifyKind(fromDt.Date, DateTimeKind.Utc);
        var endDate = DateTime.SpecifyKind(toDt.Date, DateTimeKind.Utc);
        if (endDate < start)
            (start, endDate) = (endDate, start);
        endExclusive = endDate.AddDays(1);
        error = null;
        return true;
    }

    private bool IsAdmin()
    {
        var userId = userContext.UserId;
        return AdminAuthorization.IsUserInAdminList(userId, configuration);
    }
}
