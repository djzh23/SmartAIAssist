using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Controllers;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class SpeechControllerTests
{
    private readonly Mock<ISpeechService> _speechServiceMock = new();
    private readonly Mock<ILogger<SpeechController>> _loggerMock = new();

    private SpeechController CreateController() => new(_speechServiceMock.Object, _loggerMock.Object);

    [Fact]
    public async Task TextToSpeech_EmptyText_ReturnsBadRequest()
    {
        var controller = CreateController();

        var result = await controller.TextToSpeech(new SpeechRequest("", "es"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task TextToSpeech_ValidRequest_ReturnsAudioFile()
    {
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
        _speechServiceMock
            .Setup(x => x.SynthesizeAsync(It.IsAny<SpeechRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider failed"));
        var controller = CreateController();

        var result = await controller.TextToSpeech(new SpeechRequest("Hola", "es"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);
    }
}
