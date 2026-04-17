using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Data;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class AdminControllerTests
{
    private static Mock<TokenTrackingService> CreateTrackingMock(IConfiguration config)
    {
        var opt = new Mock<IOptionsSnapshot<DatabaseFeatureOptions>>();
        opt.Setup(o => o.Value).Returns(new DatabaseFeatureOptions { PostgresEnabled = false, TokenUsageStorage = "redis" });
        return new Mock<TokenTrackingService>(
            opt.Object,
            new TokenTrackingRedisService(config, new HttpClient(), Mock.Of<ILogger<TokenTrackingRedisService>>()),
            new ServiceCollection().BuildServiceProvider());
    }

    private static Dictionary<string, string?> BaseConfig(string adminCsv) => new()
    {
        ["Admin:UserIds"] = adminCsv,
        ["Upstash:RestUrl"] = "https://redis.test",
        ["Upstash:RestToken"] = "test-token",
    };

    private static IConfiguration ConfigWithAdmins(string csv) =>
        new ConfigurationBuilder().AddInMemoryCollection(BaseConfig(csv)).Build();

    [Fact]
    public async Task GetDashboard_UserNotInAdminList_Returns403()
    {
        var config = ConfigWithAdmins("only-admin");
        var clerk = new Mock<ClerkAuthService>();
        clerk.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("normal-user", false));

        var tracking = CreateTrackingMock(config);
        var controller = new AdminController(tracking.Object, clerk.Object, config, Mock.Of<ILogger<AdminController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.GetDashboard(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        tracking.Verify(t => t.GetDashboardDataAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDashboard_UserInAdminList_ReturnsOk()
    {
        var config = ConfigWithAdmins("normal-user,other");
        var clerk = new Mock<ClerkAuthService>();
        clerk.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("normal-user", false));

        var tracking = CreateTrackingMock(config);
        tracking
            .Setup(t => t.GetDashboardDataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminDashboardData());

        var controller = new AdminController(tracking.Object, clerk.Object, config, Mock.Of<ILogger<AdminController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.GetDashboard(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<AdminDashboardData>(ok.Value);
        tracking.Verify(t => t.GetDashboardDataAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDashboard_EmptyAdminList_Returns403()
    {
        var config = ConfigWithAdmins("");
        var clerk = new Mock<ClerkAuthService>();
        clerk.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("any-user", false));

        var tracking = CreateTrackingMock(config);
        var controller = new AdminController(tracking.Object, clerk.Object, config, Mock.Of<ILogger<AdminController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.GetDashboard(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }
}
