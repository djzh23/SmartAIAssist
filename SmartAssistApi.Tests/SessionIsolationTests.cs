using SmartAssistApi.Configuration;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class SessionIsolationTests
{
    [Fact]
    public void SkillRegistry_DifferentApiTools_ResolveSeparately()
    {
        var job = SkillRegistry.FindSkill("jobanalyzer");
        var interview = SkillRegistry.FindSkill("interviewprep");
        Assert.NotNull(job);
        Assert.NotNull(interview);
        Assert.Equal("jobanalyzer", job!.ApiToolType);
        Assert.Equal("interviewprep", interview!.ApiToolType);
    }

    [Fact]
    public void PlanTiers_StarterMapsToPremiumRank()
    {
        Assert.True(PlanTiers.MeetsMinPlan("premium", "starter"));
        Assert.True(PlanTiers.MeetsMinPlan("pro", "starter"));
        Assert.False(PlanTiers.MeetsMinPlan("free", "starter"));
    }

    [Fact]
    public void AgentToolResolution_NormalizesInterviewAlias()
    {
        var r = AgentToolResolution.TryResolve("interview");
        Assert.NotNull(r);
        Assert.Equal("interviewprep", r!.ApiToolType);
    }
}
