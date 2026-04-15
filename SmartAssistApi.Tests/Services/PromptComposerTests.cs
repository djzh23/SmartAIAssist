using SmartAssistApi.Models;
using SmartAssistApi.Services;
using SmartAssistApi.Services.Groq;

namespace SmartAssistApi.Tests.Services;

public class PromptComposerTests
{
    [Fact]
    public void ComputeToggleHash_None_WhenTogglesNull()
    {
        Assert.Equal("none", PromptComposer.ComputeToggleHash(null));
    }

    [Fact]
    public void ComputeToggleHash_EncodesFlagsAndJobId()
    {
        var t = new ProfileContextToggles
        {
            IncludeBasicProfile = true,
            IncludeSkills = false,
            IncludeExperience = true,
            IncludeCv = false,
            ActiveTargetJobId = "abc123",
        };
        var h = PromptComposer.ComputeToggleHash(t);
        Assert.Equal("1010_abc123", h);
    }

    [Theory]
    [InlineData("general")]
    [InlineData("jobanalyzer")]
    [InlineData("interviewprep")]
    [InlineData("programming")]
    [InlineData("language")]
    public void GroqInference_MaxTokens_AndTemperature_AreWithinBounds(string tool)
    {
        var max = GroqInferenceParameters.MaxTokensFor(tool);
        Assert.True(max is >= 100 and <= 2000, $"max_tokens {max}");
        var temp = GroqInferenceParameters.TemperatureFor(tool);
        Assert.True(temp <= 0.7);
        var anth = GroqInferenceParameters.AnthropicTemperatureFor(tool);
        Assert.True(anth <= 0.7m);
    }
}
