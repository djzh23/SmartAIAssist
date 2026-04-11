using Microsoft.Extensions.Configuration;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class CareerProfileServiceTests
{
    private static CareerProfileService CreateService()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Upstash:RestUrl"] = "https://example.invalid",
            ["Upstash:RestToken"] = "test",
        }).Build();
        return new CareerProfileService(config, new HttpClient());
    }

    [Fact]
    public void BuildProfileContext_WithBasicProfile_ReturnsFormattedString()
    {
        var service = CreateService();
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

        var result = service.BuildProfileContext(profile, toggles);

        Assert.Contains("[NUTZERPROFIL]", result);
        Assert.Contains("IT / Softwareentwicklung", result);
        Assert.Contains("1-3 Jahre Erfahrung", result);
        Assert.Contains("Junior Frontend Developer", result);
        Assert.Contains("[ENDE NUTZERPROFIL]", result);
    }

    [Fact]
    public void BuildProfileContext_WithAllTogglesOff_ReturnsEmpty()
    {
        var service = CreateService();
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

        var result = service.BuildProfileContext(profile, toggles);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildProfileContext_WithSkills_IncludesSkillsList()
    {
        var service = CreateService();
        var profile = new CareerProfile
        {
            Skills = new List<string> { "React", "TypeScript", "Node.js" },
        };
        var toggles = new ProfileContextToggles
        {
            IncludeBasicProfile = false,
            IncludeSkills = true,
        };

        var result = service.BuildProfileContext(profile, toggles);

        Assert.Contains("React, TypeScript, Node.js", result);
    }

    [Fact]
    public void BuildProfileContext_WithTargetJob_IncludesJobDetails()
    {
        var service = CreateService();
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

        var result = service.BuildProfileContext(profile, toggles);

        Assert.Contains("Senior Developer", result);
        Assert.Contains("SAP", result);
        Assert.Contains("React, TypeScript required", result);
    }

    [Fact]
    public void BuildProfileContext_CvText_LimitedTo500CharsInExcerpt()
    {
        var service = CreateService();
        var longCv = new string('X', 1000);
        var profile = new CareerProfile { CvRawText = longCv };
        var toggles = new ProfileContextToggles
        {
            IncludeBasicProfile = false,
            IncludeCv = true,
        };

        var result = service.BuildProfileContext(profile, toggles);

        Assert.True(result.Length < 700, $"Expected bounded context length, got {result.Length}");
        Assert.Contains("CV-Auszug:", result);
    }
}
