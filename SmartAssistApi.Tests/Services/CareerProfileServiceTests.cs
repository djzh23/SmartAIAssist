using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class CareerProfileServiceTests
{
    [Fact]
    public void BuildProfileContext_WithBasicProfile_ReturnsFormattedString()
    {
        var profile = new CareerProfile
        {
            Field = "it",
            FieldLabel = "IT / Softwareentwicklung",
            Level = "junior",
            LevelLabel = "1-3 Jahre Erfahrung",
            CurrentRole = "Junior Frontend Developer",
            Goals = new List<string> { "new_job", "interview_prep" },
        };
        var toggles = new ProfileContextToggles { IncludeBasicProfile = true };

        var result = CareerProfileContextBuilder.Build(profile, toggles);

        Assert.Contains("[NUTZERPROFIL]", result);
        Assert.Contains("IT / Softwareentwicklung", result);
        Assert.Contains("1-3 Jahre Erfahrung", result);
        Assert.Contains("Junior Frontend Developer", result);
        Assert.Contains("[ENDE NUTZERPROFIL]", result);
    }

    [Fact]
    public void BuildProfileContext_WithAllTogglesOff_ReturnsEmpty()
    {
        var profile = new CareerProfile
        {
            FieldLabel = "IT",
            Skills = new List<string> { "React" },
        };
        var toggles = new ProfileContextToggles
        {
            IncludeBasicProfile = false,
            IncludeSkills = false,
            IncludeExperience = false,
            IncludeCv = false,
            ActiveTargetJobId = null,
        };

        var result = CareerProfileContextBuilder.Build(profile, toggles);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildProfileContext_WithSkills_IncludesSkillsList()
    {
        var profile = new CareerProfile
        {
            Skills = new List<string> { "React", "TypeScript", "Node.js" },
        };
        var toggles = new ProfileContextToggles
        {
            IncludeBasicProfile = false,
            IncludeSkills = true,
        };

        var result = CareerProfileContextBuilder.Build(profile, toggles);

        Assert.Contains("React, TypeScript, Node.js", result);
    }

    [Fact]
    public void BuildProfileContext_WithTargetJob_IncludesJobDetails()
    {
        var profile = new CareerProfile
        {
            TargetJobs = new List<TargetJob>
            {
                new() { Id = "abc12345", Title = "Senior Developer", Company = "SAP", Description = "React, TypeScript required" },
            },
        };
        var toggles = new ProfileContextToggles
        {
            IncludeBasicProfile = false,
            ActiveTargetJobId = "abc12345",
        };

        var result = CareerProfileContextBuilder.Build(profile, toggles);

        Assert.Contains("Senior Developer", result);
        Assert.Contains("SAP", result);
        Assert.Contains("React, TypeScript required", result);
    }

    [Fact]
    public void BuildProfileContext_CvText_LimitedTo500CharsInExcerpt()
    {
        var longCv = new string('X', 1000);
        var profile = new CareerProfile { CvRawText = longCv };
        var toggles = new ProfileContextToggles
        {
            IncludeBasicProfile = false,
            IncludeCv = true,
        };

        var result = CareerProfileContextBuilder.Build(profile, toggles);

        Assert.True(result.Length < 700, $"Expected bounded context length, got {result.Length}");
        Assert.Contains("CV (Auszug", result);
    }

    [Fact]
    public void CareerProfile_JsonRoundtrip_PreservesLists()
    {
        var original = new CareerProfile
        {
            UserId = "user_test",
            FieldLabel = "IT",
            Skills = new List<string> { "A", "B" },
            Goals = new List<string> { "g1" },
        };
        var json = System.Text.Json.JsonSerializer.Serialize(original, CareerProfileRedisService.JsonOpts);
        var back = System.Text.Json.JsonSerializer.Deserialize<CareerProfile>(json, CareerProfileRedisService.JsonOpts);
        Assert.NotNull(back);
        Assert.Equal(2, back!.Skills.Count);
        Assert.Equal("A", back.Skills[0]);
    }
}
