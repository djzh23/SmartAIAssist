using Microsoft.Extensions.Logging.Abstractions;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class ApplicationServiceTests
{
    private readonly MemoryRedisStringStore _redis = new();
    private readonly ApplicationService _service;

    public ApplicationServiceTests()
    {
        _service = new ApplicationService(_redis, NullLogger<ApplicationService>.Instance);
    }

    [Fact]
    public async Task CreateApplication_Success()
    {
        var app = await _service.CreateApplication("user1", "Dev", "SAP", null, null);
        Assert.Equal("Dev", app.JobTitle);
        Assert.Equal(ApplicationStatus.Draft, app.Status);
        Assert.Single(app.Timeline);
        Assert.Equal("Bewerbung angelegt", app.Timeline[0].Description);
    }

    [Fact]
    public async Task UpdateStatus_AddsTimelineEntry()
    {
        var app = await _service.CreateApplication("user1", "Dev", "SAP", null, null);
        await _service.UpdateStatus("user1", app.Id, ApplicationStatus.Applied, null);

        var apps = await _service.GetApplications("user1");
        Assert.Equal(ApplicationStatus.Applied, apps[0].Status);
        Assert.Equal(2, apps[0].Timeline.Count);
    }

    [Fact]
    public async Task Applications_DifferentUsers_AreIsolated()
    {
        await _service.CreateApplication("user_A", "Dev", "SAP", null, null);
        await _service.CreateApplication("user_B", "PM", "BMW", null, null);

        var appsA = await _service.GetApplications("user_A");
        var appsB = await _service.GetApplications("user_B");

        Assert.Single(appsA);
        Assert.Equal("Dev", appsA[0].JobTitle);
        Assert.Single(appsB);
        Assert.Equal("PM", appsB[0].JobTitle);
    }
}
