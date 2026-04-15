using Microsoft.Extensions.Configuration;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class AgentModelSelectorTests
{
    [Theory]
    [InlineData("general")]
    [InlineData("language")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jobanalyzer")]
    [InlineData("interviewprep")]
    [InlineData("programming")]
    public void UsesHaikuModel_DefaultsToTrue(string tool)
    {
        Assert.True(AgentModelSelector.UsesHaikuModel(tool));
    }

    [Fact]
    public void ResolveModel_UsesHaikuByDefault_ForAllTools()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("general", config));
        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("language", config));
        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("jobanalyzer", config));
        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("programming", config));
    }

    [Fact]
    public void ResolveModel_RespectsAnthropicHaikuModel()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:HaikuModel"] = "custom-haiku-id" })
            .Build();

        Assert.Equal("custom-haiku-id", AgentModelSelector.ResolveModel("general", config));
    }

    [Fact]
    public void ResolveModel_SonnetTools_OptsInSingleTool()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:Model"] = "claude-sonnet-4-20250514",
                ["Anthropic:SonnetTools"] = "jobanalyzer",
            })
            .Build();

        Assert.Equal("claude-sonnet-4-20250514", AgentModelSelector.ResolveModel("jobanalyzer", config));
        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("interviewprep", config));
    }

    [Fact]
    public void ResolveModel_SonnetTools_Star_ForcesSonnetForAll()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Anthropic:Model"] = "claude-sonnet-4-20250514",
                ["Anthropic:SonnetTools"] = "*",
            })
            .Build();

        Assert.Equal("claude-sonnet-4-20250514", AgentModelSelector.ResolveModel("programming", config));
    }
}
