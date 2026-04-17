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

public class SpeechControllerTests
{
    private readonly Mock<ISpeechService> _speechServiceMock = new();
    private readonly Mock<ClerkAuthService> _clerkMock = new();
    private readonly Mock<UsageService> _usageMock;
    private readonly Mock<ILogger<SpeechController>> _loggerMock = new();

    public SpeechControllerTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Upstash:RestUrl"] = "https://fake.upstash.io",
                ["Upstash:RestToken"] = "fake-token",
            })
            .Build();
        var usageOpts = new Mock<IOptionsSnapshot<DatabaseFeatureOptions>>();
        usageOpts.Setup(o => o.Value).Returns(new DatabaseFeatureOptions { PostgresEnabled = false, UsageStorage = "redis", TokenUsageStorage = "redis" });
        _usageMock = new Mock<UsageService>(usageOpts.Object, new UsageRedisService(config, new HttpClient()), new ServiceCollection().BuildServiceProvider());
    }

    private SpeechController CreateController() =>
        new(_speechServiceMock.Object, _clerkMock.Object, _usageMock.Object, _loggerMock.Object);

    [Fact]
    public async Task TextToSpeech_EmptyText_ReturnsBadRequest()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("user_x", false));

        var controller = CreateController();

        var result = await controller.TextToSpeech(new SpeechRequest("", "es"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task TextToSpeech_ValidRequest_ReturnsAudioFile()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("user_x", false));

        var bytes = new byte[] { 1, 2, 3, 4 };
        _speechServiceMock
            .Setup(x => x.SynthesizeAsync(It.IsAny<SpeechRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeechResult(bytes, "audio/mpeg"));
        var controller = CreateController();

        var result = await controller.TextToSpeech(new SpeechRequest("Hola", "es"), CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("audio/mpeg", file.ContentType);
        Assert.Equal(bytes, file.FileContents);
    }

    [Fact]
    public async Task TextToSpeech_ProviderFailure_ReturnsBadGateway()
    {
        _clerkMock.Setup(c => c.ExtractUserId(It.IsAny<HttpRequest>()))
            .Returns(("user_x", false));

        _speechServiceMock
            .Setup(x => x.SynthesizeAsync(It.IsAny<SpeechRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider failed"));
        var controller = CreateController();

        var result = await controller.TextToSpeech(new SpeechRequest("Hola", "es"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }
}
