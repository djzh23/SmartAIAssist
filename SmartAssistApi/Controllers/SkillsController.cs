using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Configuration;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("agent_read")]
public class SkillsController(
    IAppUserContext userContext,
    UsageService usageService,
    ILogger<SkillsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSkills()
    {
        try
        {
            var userId = userContext.UserId;
            var isAnonymous = userContext.IsAnonymous;
            var plan = isAnonymous ? "anonymous" : await usageService.GetPlanAsync(userId);

            var skills = SkillRegistry.GetVisibleSkills().Select(s => new
            {
                id = s.Id,
                name = s.Name,
                description = s.Description,
                icon = s.Icon,
                category = s.Category,
                badge = s.Badge,
                badgeColor = s.BadgeColor,
                isEnabled = s.IsEnabled,
                isBeta = s.IsBeta,
                minPlan = s.MinPlan,
                apiToolType = s.ApiToolType,
                isAccessible = SkillRegistry.IsToolAccessible(plan, s) && s.IsEnabled,
            });

            return Ok(skills);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GET /api/skills failed (plan read or serialization)");
            return StatusCode(503, new
            {
                error = "skills_unavailable",
                message = "Could not load skills. Storage may be temporarily unavailable.",
            });
        }
    }
}
