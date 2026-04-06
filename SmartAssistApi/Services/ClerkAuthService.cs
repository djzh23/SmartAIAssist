using System.Text.Json;

namespace SmartAssistApi.Services;

/// <summary>
/// Extracts the Clerk userId from the JWT Bearer token.
/// NOTE: This parses the JWT payload without signature verification (MVP).
/// For production, add JWKS validation against Clerk's public keys.
/// </summary>
public class ClerkAuthService
{
    public virtual (string? userId, bool isAnonymous) ExtractUserId(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var ip = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return ($"ip:{ip}", true);
        }

        try
        {
            var token  = authHeader["Bearer ".Length..];
            var parts  = token.Split('.');
            if (parts.Length != 3)
            {
                var ip = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return ($"ip:{ip}", true);
            }

            // Base64-url decode the payload (add padding if needed)
            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
                             .Replace('-', '+').Replace('_', '/');

            var json    = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims  = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var userId  = claims?.GetValueOrDefault("sub").GetString();

            if (string.IsNullOrEmpty(userId))
            {
                var ip = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return ($"ip:{ip}", true);
            }

            return (userId, false);
        }
        catch
        {
            var ip = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return ($"ip:{ip}", true);
        }
    }
}
