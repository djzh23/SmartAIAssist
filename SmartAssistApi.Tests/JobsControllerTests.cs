using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class JobsControllerTests
{
    private readonly Mock<ClerkAuthService> _clerkMock = new();
    private readonly Mock<IJobContextExtractor> _extractorMock = new();
    private readonly JobsController _controller;

    public JobsControllerTests()
    {
        _controller = new JobsController(
            _clerkMock.Object,
            _extractorMock.Object,
            NullLogger<JobsController>.Instance);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
    }

    [Fact]
    public async Task Preview_Unauthorized_WhenAnonymous()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(((string?)null, true));
        var result = await _controller.Preview(new JobPreviewRequest("https://example.com/job"));
        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Preview_OkFailure_WhenEmptyInput()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("user_1", false));
        var result = await _controller.Preview(new JobPreviewRequest("  "));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<JobPreviewResponse>(ok.Value);
        Assert.False(payload.Success);
        Assert.NotNull(payload.Error);
    }

    [Fact]
    public async Task Preview_Ok_WhenExtractorSucceeds()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("user_1", false));
        _extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<string>()))
            .ReturnsAsync(new JobContext
            {
                IsAnalyzed = true,
                JobTitle = "Dev",
                CompanyName = "Acme",
                Location = "Berlin",
                RawJobText = new string('x', 200),
                KeyRequirements = ["a"],
                Keywords = ["b"],
            });
        var result = await _controller.Preview(new JobPreviewRequest(new string('x', 200)));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<JobPreviewResponse>(ok.Value);
        Assert.True(payload.Success);
        Assert.Equal("Dev", payload.JobTitle);
        Assert.Equal("Acme", payload.CompanyName);
    }

    [Fact]
    public async Task Preview_OkFailure_WhenExtractorThrows()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>())).Returns(("user_1", false));
        _extractorMock
            .Setup(e => e.ExtractAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("too short"));
        var result = await _controller.Preview(new JobPreviewRequest("short"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<JobPreviewResponse>(ok.Value);
        Assert.False(payload.Success);
        Assert.NotNull(payload.Error);
    }
}
