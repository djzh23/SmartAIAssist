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
    private readonly Mock<IAgentService> _agentServiceMock = new();
    private readonly Mock<ILogger<AgentController>> _loggerMock = new();
    private readonly Mock<UsageService> _usageMock;
    private readonly Mock<ClerkAuthService> _clerkMock = new();
    private readonly Mock<ISpeechService> _speechMock = new();
    private readonly ConversationService _conversationService = new();

    public AgentControllerTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upstash:RestUrl"] = "https://fake.upstash.io",
                ["Upstash:RestToken"] = "fake-token",
            })
            .Build();

        _usageMock = new Mock<UsageService>(config, new HttpClient());
    }

    private AgentController CreateController()
    {
        var controller = new AgentController(
            _agentServiceMock.Object,
            _conversationService,
            _usageMock.Object,
            _clerkMock.Object,
            _speechMock.Object,
            _loggerMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task Ask_EmptyMessage_Returns400()
    {
        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest(""));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Ask_MessageOver4000Chars_Returns400()
    {
        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest(new string('x', 4001)));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Ask_MissingSessionId_Returns400()
    {
        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Hello world"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task Ask_ValidMessage_Returns200WithAgentResponse()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("user_abc", false));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("user_abc", false))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = true,
                UsageToday = 1,
                DailyLimit = 20,
                Plan = "free"
            });

        _agentServiceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
            .ReturnsAsync(new AgentResponse("Test reply", "get_weather"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("What is the weather?", SessionId: "sess-1"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentResponse>(ok.Value);
        Assert.Equal("Test reply", response.Reply);
        Assert.Equal("get_weather", response.ToolUsed);
    }

    [Fact]
    public async Task Ask_AnonymousUser_Returns200WithAgentResponse()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("ip:127.0.0.1", true));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("ip:127.0.0.1", true))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = true,
                UsageToday = 1,
                DailyLimit = 2,
                Plan = "anonymous"
            });

        _agentServiceMock.Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
            .ReturnsAsync(new AgentResponse("Berlin weather is sunny.", "get_weather"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Wie ist das Wetter in Berlin?", SessionId: "demo-1", ToolType: "general"));

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentResponse>(ok.Value);
        Assert.Equal("Berlin weather is sunny.", response.Reply);
    }

    [Fact]
    public async Task Ask_AnonymousUsageLimitReached_Returns429()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("ip:127.0.0.1", true));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("ip:127.0.0.1", true))
            .ReturnsAsync(new UsageCheckResult
            {
                Allowed = false,
                Reason = "anonymous_limit",
                Message = "Sign in to get 20 free responses per day",
                UsageToday = 2,
                DailyLimit = 2,
                Plan = "anonymous"
            });

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Third message", SessionId: "demo-1"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(429, obj.StatusCode);
    }

    [Fact]
    public async Task Ask_UsageServiceThrows_Returns503()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("ip:127.0.0.1", true));

        _usageMock.Setup(u => u.CheckAndIncrementAsync("ip:127.0.0.1", true))
            .ThrowsAsync(new InvalidOperationException("Upstash connection refused"));

        var controller = CreateController();
        var result = await controller.Ask(new AgentRequest("Hello", SessionId: "demo-1"));

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, obj.StatusCode);
    }

    [Fact]
    public async Task SetContext_MissingSessionId_Returns400()
    {
        var controller = CreateController();

        var result = await controller.SetContext(new SetContextRequest(
            SessionId: null,
            ToolType: "interviewprep",
            CVText: "my cv",
            JobTitle: "Software Engineer",
            CompanyName: "SmartAssist",
            ProgrammingLanguage: null));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task SetContext_ThenGetContext_ReturnsStoredValues()
    {
        var controller = CreateController();
        var sessionId = "s-ctx-1";

        var setResult = await controller.SetContext(new SetContextRequest(
            SessionId: sessionId,
            ToolType: "interviewprep",
            CVText: "CV data here",
            JobTitle: "Backend Developer",
            CompanyName: "Acme",
            ProgrammingLanguage: null));

        Assert.IsType<OkObjectResult>(setResult);

        var getResult = await controller.GetContext(sessionId, "interviewprep");
        var ok = Assert.IsType<OkObjectResult>(getResult);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("\"hasCV\":true", payloadJson);
        Assert.Contains("Backend Developer", payloadJson);
        Assert.Contains("Acme", payloadJson);
        Assert.Contains("\"toolType\":\"interviewprep\"", payloadJson);
    }

    [Fact]
    public async Task SetContext_ProgrammingLanguage_IsReturnedByGetContext()
    {
        var controller = CreateController();
        var sessionId = "s-ctx-2";

        var setResult = await controller.SetContext(new SetContextRequest(
            SessionId: sessionId,
            ToolType: "programming",
            CVText: null,
            JobTitle: null,
            CompanyName: null,
            ProgrammingLanguage: "csharp"));

        Assert.IsType<OkObjectResult>(setResult);

        var getResult = await controller.GetContext(sessionId, "programming");
        var ok = Assert.IsType<OkObjectResult>(getResult);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(ok.Value);

        Assert.Contains("\"hasProgrammingLang\":true", payloadJson);
        Assert.Contains("\"programmingLanguage\":\"csharp\"", payloadJson);
    }

    [Fact]
    public void Health_ReturnsOkWithStatusAndTimestamp()
    {
        var controller = CreateController();
        var result = controller.Health();

        var ok = Assert.IsType<OkObjectResult>(result);
        var payloadJson = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("ok", payloadJson);
        Assert.Contains("timestamp", payloadJson);
    }
}
