using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class AgentControllerTests
{
    private readonly Mock<IAgentService> _serviceMock = new();
    private readonly Mock<ILogger<AgentController>> _loggerMock = new();
    private AgentController CreateController() => new(_serviceMock.Object, _loggerMock.Object);

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
    public async Task Ask_MessageOver500Chars_Returns400WithCharacterCount()
    {
        var controller = CreateController();
        var longMessage = new string('x', 501);

        var result = await controller.Ask(new AgentRequest(longMessage));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var json = System.Text.Json.JsonSerializer.Serialize(badRequest.Value);
        Assert.Contains("501", json);
    }

    [Fact]
    public async Task Ask_MessageExactly500Chars_Returns200()
    {
        var message = new string('x', 500);
        _serviceMock
            .Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
            .ReturnsAsync(new AgentResponse("reply"));
        var controller = CreateController();

        var result = await controller.Ask(new AgentRequest(message));

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task Ask_ValidMessage_Returns200WithAgentResponse()
    {
        _serviceMock
            .Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
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
        _serviceMock
            .Setup(s => s.RunAsync(It.IsAny<AgentRequest>()))
            .ThrowsAsync(new Exception("API error"));
        var controller = CreateController();

        var result = await controller.Ask(new AgentRequest("Hello"));

        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(statusResult.Value);
        Assert.Contains("API error", json);
    }

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
