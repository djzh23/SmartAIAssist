using Microsoft.AspNetCore.Mvc;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LearningController(LearningMemoryService learningMemory, ClerkAuthService clerkAuth) : ControllerBase
{
    [HttpGet("insights")]
    public async Task<IActionResult> GetInsights(CancellationToken cancellationToken)
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        if (isAnonymous || string.IsNullOrEmpty(userId))
            return Unauthorized();

        var memory = await learningMemory.GetMemory(userId, cancellationToken).ConfigureAwait(false);
        return Ok(memory.Insights);
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
