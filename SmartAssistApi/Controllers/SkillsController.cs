using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartAssistApi.Configuration;
using SmartAssistApi.Services;

namespace SmartAssistApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("agent_read")]
public class SkillsController(ClerkAuthService clerkAuth, UsageService usageService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSkills()
    {
        var (userId, isAnonymous) = clerkAuth.ExtractUserId(Request);
        var plan = isAnonymous ? "anonymous" : await usageService.GetPlanAsync(userId!);

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
}
