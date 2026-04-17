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
    ClerkAuthService clerkAuth,
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
            return Forbid();

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
            return Forbid();

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
            return Forbid();

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
            return Forbid();

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
            return Forbid();

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
            return Forbid();

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
            return Forbid();

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
            return Forbid();

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

    private bool IsAdmin()
    {
        var (userId, _) = clerkAuth.ExtractUserId(Request);
        return AdminAuthorization.IsUserInAdminList(userId, configuration);
    }
}
