using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("agent_read")]
public class LearningController(LearningMemoryService learningMemory, ClerkAuthService clerkAuth) : ControllerBase
{
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights(
        [FromQuery] string? applicationId,
        [FromQuery] bool includeResolved = false,
        CancellationToken cancellationToken = default)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var memory = await learningMemory.GetMemory(userId, cancellationToken).ConfigureAwait(false);
        IEnumerable<LearningInsight> query = memory.Insights;
        if (!includeResolved)
            query = query.Where(i => !i.Resolved);
        if (!string.IsNullOrWhiteSpace(applicationId))
        {
            query = query.Where(i =>
                string.Equals(i.JobApplicationId, applicationId, StringComparison.Ordinal));
        }

        var rows = query
            .OrderBy(i => i.SortOrder)
            .ThenByDescending(i => i.UpdatedAt == default ? i.CreatedAt : i.UpdatedAt)
            .ToList();
        return Ok(rows);
    }

    public sealed record PatchInsightBody(
        [StringLength(200)] string? Title,
        [StringLength(8000)] string? Content,
        bool? Resolved);

    [HttpPatch("insights/{insightId}")]
    public async Task<IActionResult> PatchInsight(string insightId, [FromBody] PatchInsightBody body, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await learningMemory
            .PatchInsight(userId, insightId, body.Title, body.Content, body.Resolved, cancellationToken)
            .ConfigureAwait(false);
        return Ok(new { success = true });
    }

    [HttpPost("insights/{insightId}/resolve")]
    public async Task<IActionResult> ResolveInsight(string insightId, CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        await learningMemory.ResolveInsight(userId, insightId, cancellationToken).ConfigureAwait(false);
        return Ok(new { success = true });
    }
}
