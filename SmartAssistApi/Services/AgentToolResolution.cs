using SmartAssistApi.Configuration;

namespace SmartAssistApi.Services;

public static class AgentToolResolution
{
    public sealed record ResolvedTool(CareerSkill Skill, string ApiToolType);

    /// <summary>Maps client tool string to a registry skill and API tool type. Returns null if unknown.</summary>
    public static ResolvedTool? TryResolve(string? toolTypeRaw)
    {
        var skill = SkillRegistry.FindSkill(toolTypeRaw);
        if (skill is null || string.IsNullOrWhiteSpace(skill.ApiToolType))
            return null;
        return new ResolvedTool(skill, skill.ApiToolType);
    }
}
