using System.Security.Cryptography;
using System.Text;

namespace SmartAssistApi.Security;

/// <summary>Stable partition key for rate limiting (per bearer token hash or client IP).</summary>
public static class ClientPartitionKey
{
    public static string Get(HttpContext httpContext)
    {
        var auth = httpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(auth))
            return $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "na"}";

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(auth));
        return $"t:{Convert.ToHexString(hash.AsSpan()[..16])}";
    }
}
