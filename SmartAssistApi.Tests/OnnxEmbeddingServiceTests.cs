using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmartAssistApi.Configuration;
using SmartAssistApi.Services.Embeddings;

namespace SmartAssistApi.Tests;

public class OnnxEmbeddingServiceTests
{
    [Fact]
    public void Constructor_DoesNotThrow_WhenVocabMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"smartassist-onnx-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var env = new Mock<IWebHostEnvironment>();
            env.Setup(e => e.ContentRootPath).Returns(tempRoot);
            var options = Options.Create(new EmbeddingOptions
            {
                ModelPath = "Models/model-2.onnx",
                VocabPath = "Models/vocab.txt",
                Dimension = 384,
            });

            var ex = Record.Exception(() => new OnnxEmbeddingService(env.Object, options, Mock.Of<ILogger<OnnxEmbeddingService>>()));
            Assert.Null(ex);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task EmbedAsync_ThrowsClearError_WhenVocabMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"smartassist-onnx-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var env = new Mock<IWebHostEnvironment>();
            env.Setup(e => e.ContentRootPath).Returns(tempRoot);
            var options = Options.Create(new EmbeddingOptions
            {
                ModelPath = "Models/model-2.onnx",
                VocabPath = "Models/vocab.txt",
                Dimension = 384,
            });

            var service = new OnnxEmbeddingService(env.Object, options, Mock.Of<ILogger<OnnxEmbeddingService>>());
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmbedAsync("hello world"));
            Assert.Contains("Embedding vocab file not found", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);
        }
    }
}
