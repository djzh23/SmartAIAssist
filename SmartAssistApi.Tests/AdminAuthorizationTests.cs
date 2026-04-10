using Microsoft.Extensions.Configuration;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class AdminAuthorizationTests
{
    [Fact]
    public void IsUserInAdminList_NoConfiguredAdmins_ReturnsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:UserIds"] = "" })
            .Build();

        Assert.False(AdminAuthorization.IsUserInAdminList("any-user", config));
        Assert.False(AdminAuthorization.IsUserInAdminList("test-user-123", config));
    }

    [Fact]
    public void IsUserInAdminList_MatchingUserId_ReturnsTrue()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:UserIds"] = "test-user-123, other" })
            .Build();

        Assert.True(AdminAuthorization.IsUserInAdminList("test-user-123", config));
        Assert.True(AdminAuthorization.IsUserInAdminList("other", config));
    }

    [Fact]
    public void IsUserInAdminList_NonMatchingUserId_ReturnsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:UserIds"] = "admin-user" })
            .Build();

        Assert.False(AdminAuthorization.IsUserInAdminList("normal-user", config));
    }

    [Fact]
    public void IsUserInAdminList_NullOrEmptyUserId_ReturnsFalse()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:UserIds"] = "admin" })
            .Build();

        Assert.False(AdminAuthorization.IsUserInAdminList(null, config));
        Assert.False(AdminAuthorization.IsUserInAdminList("", config));
        Assert.False(AdminAuthorization.IsUserInAdminList("   ", config));
    }
}
