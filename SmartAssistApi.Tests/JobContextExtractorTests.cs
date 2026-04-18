using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class JobContextExtractorTests
{
    [Fact]
    public async Task ExtractAsync_Url_PrefersJsonLdStructuredMeta()
    {
        var desc = "<p>" + new string('x', 130) + "</p>";
        var html =
            "<html><head><script type=\"application/ld+json\">"
            + "{\"@type\":\"JobPosting\",\"title\":\"Marketing Consultant\",\"description\":"
            + System.Text.Json.JsonSerializer.Serialize(desc)
            + ",\"hiringOrganization\":{\"name\":\"KPMG AG\"}"
            + ",\"jobLocation\":{\"address\":{\"addressLocality\":\"Frankfurt\"}}}"
            + "</script></head></html>";

        var handler = new StubHttpMessageHandler(html);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var extractor = new JobContextExtractor(factory.Object, NullLogger<JobContextExtractor>.Instance);
        var ctx = await extractor.ExtractAsync("https://example.com/job");

        Assert.Equal("Marketing Consultant", ctx.JobTitle);
        Assert.Equal("KPMG AG", ctx.CompanyName);
        Assert.Equal("Frankfurt", ctx.Location);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _html;
        public StubHttpMessageHandler(string html) => _html = html;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_html, Encoding.UTF8, "text/html"),
            });
        }
    }
}
