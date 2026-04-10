using Microsoft.Extensions.Configuration;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class AgentModelSelectorTests
{
    [Theory]
    [InlineData("general", true)]
    [InlineData("language", true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("jobanalyzer", false)]
    [InlineData("interviewprep", false)]
    [InlineData("programming", false)]
    [InlineData("weather", false)]
    public void UsesHaikuModel_ExpectedForTool(string tool, bool expectHaiku)
    {
        Assert.Equal(expectHaiku, AgentModelSelector.UsesHaikuModel(tool));
    }

    [Fact]
    public void ResolveModel_HaikuTools_UsesHaikuModelIdOrConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("general", config));
        Assert.Equal(AgentModelSelector.DefaultHaikuModelId, AgentModelSelector.ResolveModel("language", config));
    }

    [Fact]
    public void ResolveModel_HaikuTools_RespectsAnthropicHaikuModel()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:HaikuModel"] = "custom-haiku-id" })
            .Build();

        Assert.Equal("custom-haiku-id", AgentModelSelector.ResolveModel("general", config));
    }

    [Fact]
    public void ResolveModel_OtherTools_UsesSonnetModelIdOrConfig()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        Assert.Equal(AgentModelSelector.DefaultSonnetModelId, AgentModelSelector.ResolveModel("jobanalyzer", config));
    }

    [Fact]
    public void ResolveModel_OtherTools_RespectsAnthropicModel()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Anthropic:Model"] = "claude-sonnet-4-20250514" })
            .Build();

        Assert.Equal("claude-sonnet-4-20250514", AgentModelSelector.ResolveModel("programming", config));
    }
}
