using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController(
    TokenTrackingService tracking,
    ClerkAuthService clerkAuth,
    IConfiguration configuration,
    ILogger<AdminController> logger) : ControllerBase
{
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
    public async Task<IActionResult> GetTopUsers([FromQuery] string? date, [FromQuery] int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!IsAdmin())
            return Forbid();

        var d = date ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

        try
        {
            var data = await tracking.GetTopUsersAsync(d, limit, cancellationToken).ConfigureAwait(false);
            return Ok(data);
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
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var configured = configuration["Admin:UserIds"]
            ?? Environment.GetEnvironmentVariable("ADMIN_USER_IDS")
            ?? string.Empty;

        var admins = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return admins.Length > 0 && admins.Contains(userId, StringComparer.Ordinal);
    }
}
