using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

internal static class TestHelpers
{
    internal static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    internal static Mock<ClerkAuthService> MockClerkAuth() =>
        new(EmptyConfig(), new Mock<ILogger<ClerkAuthService>>().Object);
}
