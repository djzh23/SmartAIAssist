using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class AgentControllerTests
{
    private readonly Mock<IAgentService>          _serviceMock  = new();
    private readonly Mock<ILogger<AgentController>> _loggerMock = new();
    private readonly Mock<UsageService>           _usageMock;
    private readonly Mock<ClerkAuthService>       _clerkMock    = new();

    public AgentControllerTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upstash:RestUrl"]   = "https://fake.upstash.io",
                ["Upstash:RestToken"] = "fake-token",
            })
            .Build();

        _usageMock = new Mock<UsageService>(config, new HttpClient());
    }

    private AgentController CreateController()
    {
        var controller = new AgentController(
            _serviceMock.Object,
            _usageMock.Object,
            _clerkMock.Object,
            _loggerMock.Object);

        // Provide a minimal HttpContext so controller can resolve IP etc.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        return controller;
    }

    // ── Validation ────────────────────────────────────────

    [Fact]
    public async Task Ask_EmptyMessage_Returns400WithErrorMessage()
    {
        var controller = CreateController();

        var result = await controller.Ask(new AgentRequest(""));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("Message must not be empty", json);
    }

    [Fact]
    public async Task Ask_WhitespaceMessage_Returns400()
    {
        var controller = CreateController();

        var result = await controller.Ask(new AgentRequest("   "));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Ask_MessageOver4000Chars_Returns400WithCharacterCount()
    {
        var controller  = CreateController();
        var longMessage = new string('x', 4001);

        var result = await controller.Ask(new AgentRequest(longMessage));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var json = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("4001", json);
    }

    [Fact]
    public async Task Ask_MessageExactly4000Chars_PassesValidation()
    {
        var message = new string('x', 4000);

        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
                  .Returns(("user_abc", false));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
                  .ReturnsAsync(new UsageCheckResult { Allowed = true, UsageToday = 1, DailyLimit = 20, Plan = "free" });

        _serviceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
                    .ReturnsAsync(new AgentResponse("reply"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest(message));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── Auth & Usage ──────────────────────────────────────

    [Fact]
    public async Task Ask_UsageLimitReached_Returns429()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
                  .Returns(("user_abc", false));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
                  .ReturnsAsync(new UsageCheckResult
                  {
                      Allowed    = false,
                      Reason     = "free_limit",
                      Message    = "Upgrade to Premium",
                      UsageToday = 20,
                      DailyLimit = 20,
                      Plan       = "free",
                  });

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Hello"));

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(429, status.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(status.Value);
        Assert.Contains("usage_limit_reached", json);
    }

    [Fact]
    public async Task Ask_ValidMessage_Returns200WithAgentResponse()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
                  .Returns(("user_abc", false));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
                  .ReturnsAsync(new UsageCheckResult { Allowed = true, UsageToday = 1, DailyLimit = 20, Plan = "free" });

        _serviceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
                    .ReturnsAsync(new AgentResponse("Test reply", "get_weather"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("What is the weather?"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentResponse>(ok.Value);
        Assert.Equal("Test reply", response.Reply);
        Assert.Equal("get_weather", response.ToolUsed);
    }

    [Fact]
    public async Task Ask_ServiceThrowsException_Returns500WithErrorDetails()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
                  .Returns(("user_abc", false));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
                  .ReturnsAsync(new UsageCheckResult { Allowed = true, UsageToday = 1, DailyLimit = 20, Plan = "free" });

        _serviceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
                    .ThrowsAsync(new Exception("API error"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Hello"));

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(statusResult.Value);
        Assert.Contains("API error", json);
    }

    // ── Health ────────────────────────────────────────────

    [Fact]
    public void Health_ReturnsOkWithStatusAndTimestamp()
    {
        var controller = CreateController();

        var result = controller.Health();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("ok", json);
        Assert.Contains("timestamp", json);
    }
}
