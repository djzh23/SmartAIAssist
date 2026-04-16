using Microsoft.AspNetCore.Http;
using SmartAssistApi.Security;

namespace SmartAssistApi.Tests.Security;

public class ClientPartitionKeyTests
{
    [Fact]
    public void Get_WithoutAuthorization_UsesIpPrefix()
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");

        var key = ClientPartitionKey.Get(ctx);

        Assert.Equal("ip:203.0.113.10", key);
    }

    [Fact]
    public void Get_WithSameBearerToken_ReturnsSamePartition()
    {
        const string auth = "Bearer same.token.value";
        var ctx1 = new DefaultHttpContext();
        ctx1.Request.Headers.Authorization = auth;
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Headers.Authorization = auth;

        Assert.Equal(ClientPartitionKey.Get(ctx1), ClientPartitionKey.Get(ctx2));
    }

    [Fact]
    public void Get_WithDifferentBearer_ReturnsDifferentPartition()
    {
        var ctx1 = new DefaultHttpContext();
        ctx1.Request.Headers.Authorization = "Bearer a";
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Headers.Authorization = "Bearer b";

        Assert.NotEqual(ClientPartitionKey.Get(ctx1), ClientPartitionKey.Get(ctx2));
    }
}
