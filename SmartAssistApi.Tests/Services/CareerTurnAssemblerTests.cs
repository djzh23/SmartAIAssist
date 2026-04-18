using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class CareerTurnAssemblerTests
{
    [Fact]
    public void ComposeEffectiveUserMessage_WithoutSetup_ReturnsPrimaryOnly()
    {
        var req = new AgentRequest("Hallo", SessionId: "s1", ToolType: "jobanalyzer");
        var composed = CareerTurnAssembler.ComposeEffectiveUserMessage(req, "jobanalyzer", "Hallo");
        Assert.Equal("Hallo", composed);
    }

    [Fact]
    public void ComposeEffectiveUserMessage_WithSetup_IncludesBlocksAndQuestion()
    {
        var setup = new CareerToolSetup(
            CvText: "Skill: C#",
            JobText: new string('j', 120),
            JobTitle: "Dev",
            CompanyName: "Acme");
        var req = new AgentRequest("Passt mein Profil?", SessionId: "s1", ToolType: "jobanalyzer", CareerToolSetup: setup);
        var composed = CareerTurnAssembler.ComposeEffectiveUserMessage(req, "jobanalyzer", "Passt mein Profil?");
        Assert.Contains("[SETUP_CV]", composed);
        Assert.Contains("[SETUP_JOB]", composed);
        Assert.Contains("[AKTUELLE_NUTZERFRAGE]", composed);
        Assert.Contains("Passt mein Profil?", composed);
        Assert.Contains("[ENDE_AKTUELLE_NUTZERFRAGE]", composed);
    }

    [Fact]
    public void ComposeEffectiveUserMessage_GeneralCoaching_IncludesMetaEvenWithSparseSetup()
    {
        var setup = new CareerToolSetup(GeneralCoaching: true);
        var req = new AgentRequest("Los gehts", SessionId: "s1", ToolType: "interviewprep", CareerToolSetup: setup);
        var composed = CareerTurnAssembler.ComposeEffectiveUserMessage(req, "interviewprep", "Los gehts");
        Assert.Contains("[META]", composed);
        Assert.Contains("Allgemeines Coaching", composed, StringComparison.Ordinal);
    }
}
