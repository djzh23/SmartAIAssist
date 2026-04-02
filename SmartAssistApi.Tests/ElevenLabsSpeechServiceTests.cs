using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class ElevenLabsSpeechServiceTests
{
    [Fact]
    public async Task SynthesizeAsync_MissingApiKey_ThrowsInvalidOperationException()
    {
        var service = new ElevenLabsSpeechService(new HttpClient(new HttpClientHandler()), BuildConfig(new Dictionary<string, string?>()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SynthesizeAsync(new SpeechRequest("Hola", "es")));

        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SynthesizeAsync_MissingVoiceId_ThrowsInvalidOperationException()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ELEVENLABS_API_KEY"] = "test-key"
        });
        var service = new ElevenLabsSpeechService(new HttpClient(new HttpClientHandler()), config);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SynthesizeAsync(new SpeechRequest("Hola", "es")));

        Assert.Contains("voice ID", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SynthesizeAsync_ValidConfiguration_ReturnsAudioPayload()
    {
        var expected = Encoding.UTF8.GetBytes("fake-mp3");
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Contains("/v1/text-to-speech/voice-es", req.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
                Assert.True(req.Headers.Contains("xi-api-key"));
                var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.DoesNotContain("language_code", body, StringComparison.OrdinalIgnoreCase);

                var content = new ByteArrayContent(expected);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                };
            });

        var http = new HttpClient(handlerMock.Object);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ELEVENLABS_API_KEY"] = "test-key",
            ["ElevenLabs:VoiceIds:es"] = "voice-es",
            ["ElevenLabs:ModelId"] = "eleven_multilingual_v2",
            ["ElevenLabs:OutputFormat"] = "mp3_44100_128"
        });

        var service = new ElevenLabsSpeechService(http, config);
        var result = await service.SynthesizeAsync(new SpeechRequest("Hola mundo", "es"));

        Assert.Equal("audio/mpeg", result.ContentType);
        Assert.Equal(expected, result.Audio);
    }

    [Fact]
    public async Task SynthesizeAsync_LanguageWithRegion_UsesBaseLanguageVoice()
    {
        var expected = Encoding.UTF8.GetBytes("fake-mp3");
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                Assert.Contains("/v1/text-to-speech/voice-es", req.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
                var content = new ByteArrayContent(expected);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        var http = new HttpClient(handlerMock.Object);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ELEVENLABS_API_KEY"] = "test-key",
            ["ElevenLabs:VoiceIds:es"] = "voice-es"
        });

        var service = new ElevenLabsSpeechService(http, config);
        var result = await service.SynthesizeAsync(new SpeechRequest("Hola mundo", "es-ES"));

        Assert.Equal(expected, result.Audio);
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
