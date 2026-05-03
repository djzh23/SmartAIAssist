using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Tests;

internal static class TestHelpers
{
    internal static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();
}
