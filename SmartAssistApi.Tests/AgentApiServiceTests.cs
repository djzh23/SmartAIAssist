using System.Net;
using System.Text.Json;
using Moq;
using Moq.Protected;
using SmartAssistApi.Client.Models;
using SmartAssistApi.Client.Services;

namespace SmartAssistApi.Tests;

public class AgentApiServiceTests
{
    private static HttpClient BuildHttpClient(HttpStatusCode status, object? body = null)
    {
        var json = body is null ? "{}" : JsonSerializer.Serialize(body);
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = status,
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });

        return new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8080/")
        };
    }

    [Fact]
    public async Task AskAsync_ValidMessage_ReturnsAgentResponse()
    {
        var expected = new AgentResponse("Hello!", null);
        var http = BuildHttpClient(HttpStatusCode.OK, expected);
        var service = new AgentApiService(http);

        var result = await service.AskAsync("Hi");

        Assert.Equal("Hello!", result.Reply);
        Assert.Null(result.ToolUsed);
    }

    [Fact]
    public async Task AskAsync_ResponseWithToolUsed_ReturnsToolName()
    {
        var expected = new AgentResponse("18°C in Berlin.", "get_weather");
        var http = BuildHttpClient(HttpStatusCode.OK, expected);
        var service = new AgentApiService(http);

        var result = await service.AskAsync("Weather in Berlin?", "abc12345");

        Assert.Equal("get_weather", result.ToolUsed);
    }

    [Fact]
    public async Task AskAsync_ServerReturns400_ThrowsHttpRequestException()
    {
        var http = BuildHttpClient(HttpStatusCode.BadRequest);
        var service = new AgentApiService(http);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync(""));
    }

    [Fact]
    public async Task AskAsync_ServerReturns500_ThrowsHttpRequestException()
    {
        var http = BuildHttpClient(HttpStatusCode.InternalServerError);
        var service = new AgentApiService(http);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.AskAsync("Hello"));
    }

    [Fact]
    public async Task DeleteSessionAsync_ValidSessionId_CompletesWithoutException()
    {
        var http = BuildHttpClient(HttpStatusCode.NoContent);
        var service = new AgentApiService(http);

        var exception = await Record.ExceptionAsync(() => service.DeleteSessionAsync("abc12345"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GenerateSpeechAsync_ValidResponse_ReturnsAudioBytes()
    {
        var bytes = new byte[] { 10, 20, 30 };
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = content
                };
            });

        var http = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:8080/")
        };
        var service = new AgentApiService(http);

        var result = await service.GenerateSpeechAsync("Hola", "es");

        Assert.Equal(bytes, result);
    }

    [Fact]
    public async Task GenerateSpeechAsync_ErrorResponse_ThrowsDetailedHttpRequestException()
    {
        var http = BuildHttpClient(HttpStatusCode.BadGateway, new { error = "Provider timeout" });
        var service = new AgentApiService(http);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GenerateSpeechAsync("Hola", "es"));

        Assert.Contains("Provider timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
