using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SmartAssistApi.Services;

/// <summary>
/// Extracts and verifies the Clerk userId from the JWT Bearer token using JWKS.
/// Falls back to unverified payload parsing when JWKS is unavailable or not configured
/// (e.g. local development without Clerk:Issuer set).
/// </summary>
public class ClerkAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<ClerkAuthService> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _oidcConfigManager;
    private readonly string? _issuer;
    private readonly bool _jwksEnabled;

    public ClerkAuthService(IConfiguration config, ILogger<ClerkAuthService> logger)
    {
        _config = config;
        _logger = logger;

        _issuer = config["Clerk:Issuer"]?.Trim();

        if (!string.IsNullOrWhiteSpace(_issuer))
        {
            var jwksUrl = $"{_issuer.TrimEnd('/')}/.well-known/openid-configuration";
            _oidcConfigManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                jwksUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
            _jwksEnabled = true;
        }
    }

    public virtual (string? userId, bool isAnonymous) ExtractUserId(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AnonymousFallback(request);
        }

        var token = authHeader["Bearer ".Length..];

        if (_jwksEnabled)
        {
            var userId = ValidateTokenWithJwks(token);
            if (userId is not null)
                return (userId, false);

            // JWKS validation failed — reject as anonymous (do NOT fall through to unverified parsing)
            _logger.LogWarning("JWT signature verification failed. Treating as anonymous.");
            return AnonymousFallback(request);
        }

        // Fallback: no Clerk:Issuer configured — parse without verification (dev only)
        return ExtractUnverified(token, request);
    }

    private string? ValidateTokenWithJwks(string token)
    {
        try
        {
            var oidcConfig = _oidcConfigManager!.GetConfigurationAsync(CancellationToken.None)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = false, // Clerk JWTs don't always include aud
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuerSigningKey = true,
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParams, out _);

            var sub = principal.FindFirst("sub")?.Value
                   ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            return string.IsNullOrWhiteSpace(sub) ? null : sub;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("JWT expired.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "JWT JWKS validation failed.");
            return null;
        }
    }

    private (string? userId, bool isAnonymous) ExtractUnverified(string token, HttpRequest request)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return AnonymousFallback(request);

            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
                             .Replace('-', '+').Replace('_', '/');

            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            var userId = claims?.GetValueOrDefault("sub").GetString();

            if (string.IsNullOrEmpty(userId))
                return AnonymousFallback(request);

            return (userId, false);
        }
        catch
        {
            return AnonymousFallback(request);
        }
    }

    private static (string? userId, bool isAnonymous) AnonymousFallback(HttpRequest request)
    {
        var ip = request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return ($"ip:{ip}", true);
    }
}
