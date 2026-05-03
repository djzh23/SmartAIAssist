using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SmartAssistApi.Services;

/// <summary>
/// Extracts and verifies the Clerk userId from the JWT Bearer token using JWKS.
/// Falls back to unverified payload parsing when JWKS is unavailable or not configured.
/// </summary>
public class ClerkAuthService
{
    private readonly ILogger<ClerkAuthService> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _oidcConfigManager;
    private readonly string? _issuer;
    private readonly bool _jwksEnabled;

    // Cached OIDC config — fetched once at startup, refreshed automatically by ConfigurationManager
    private OpenIdConnectConfiguration? _cachedOidcConfig;

    public ClerkAuthService(IConfiguration config, ILogger<ClerkAuthService> logger)
    {
        _logger = logger;

        _issuer = config["Clerk:Issuer"]?.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(_issuer))
        {
            var jwksUrl = $"{_issuer}/.well-known/openid-configuration";
            _oidcConfigManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                jwksUrl,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = _issuer.StartsWith("https", StringComparison.OrdinalIgnoreCase) });
            _jwksEnabled = true;

            logger.LogInformation("ClerkAuthService: JWKS enabled. Issuer={Issuer} OIDC={OidcUrl}", _issuer, jwksUrl);
        }
        else
        {
            logger.LogWarning("ClerkAuthService: No Clerk:Issuer configured. JWT verification DISABLED (unverified parsing only).");
        }
    }

    /// <summary>
    /// Pre-fetches JWKS keys during application startup so that subsequent
    /// synchronous ExtractUserId calls never block on a network request.
    /// Call this from Program.cs after building the app.
    /// </summary>
    public async Task WarmupAsync()
    {
        if (!_jwksEnabled || _oidcConfigManager is null) return;
        try
        {
            _cachedOidcConfig = await _oidcConfigManager.GetConfigurationAsync(CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogInformation("ClerkAuthService: JWKS keys pre-fetched successfully. Keys={KeyCount}",
                _cachedOidcConfig.SigningKeys?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClerkAuthService: Failed to pre-fetch JWKS keys. JWT verification will retry on first request.");
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
            var userId = ValidateTokenWithJwksAsync(token).GetAwaiter().GetResult();
            if (userId is not null)
                return (userId, false);

            _logger.LogWarning("JWT signature verification failed. Treating as anonymous.");
            return AnonymousFallback(request);
        }

        // No Clerk:Issuer configured — parse without verification (dev only)
        return ExtractUnverified(token, request);
    }

    /// <summary>Async version for use in middleware or async contexts.</summary>
    public virtual async Task<(string? userId, bool isAnonymous)> ExtractUserIdAsync(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AnonymousFallback(request);
        }

        var token = authHeader["Bearer ".Length..];

        if (_jwksEnabled)
        {
            var userId = await ValidateTokenWithJwksAsync(token);
            if (userId is not null)
                return (userId, false);

            _logger.LogWarning("JWT signature verification failed. Treating as anonymous.");
            return AnonymousFallback(request);
        }

        return ExtractUnverified(token, request);
    }

    private async Task<string?> ValidateTokenWithJwksAsync(string token)
    {
        try
        {
            var oidcConfig = _cachedOidcConfig
                ?? await _oidcConfigManager!.GetConfigurationAsync(CancellationToken.None)
                    .ConfigureAwait(false);

            if (oidcConfig.SigningKeys == null || !oidcConfig.SigningKeys.Any())
            {
                _logger.LogError("JWKS returned no signing keys from {Issuer}", _issuer);
                return null;
            }

            // Log token metadata for diagnostics (first failure only — avoid log spam)
            try
            {
                var peek = new JwtSecurityTokenHandler().ReadJwtToken(token);
                _logger.LogInformation("JWT peek: iss={Iss} kid={Kid} exp={Exp} nbf={Nbf} keys_available={Keys}",
                    peek.Issuer, peek.Header.Kid,
                    peek.ValidTo.ToString("o"), peek.ValidFrom.ToString("o"),
                    oidcConfig.SigningKeys.Count());
            }
            catch { /* best-effort diagnostics */ }

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                // Also accept issuer with trailing slash (Clerk inconsistency)
                ValidIssuers = [_issuer!, $"{_issuer}/"],
                ValidateAudience = false,
                ValidateLifetime = false, // Clerk session tokens are short-lived (60s); frontend handles refresh via getToken()
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuerSigningKey = true,
            };

            var handler = new JwtSecurityTokenHandler();
            handler.InboundClaimTypeMap.Clear(); // Prevent "sub" → ClaimTypes.NameIdentifier remapping
            var principal = handler.ValidateToken(token, validationParams, out var validatedToken);

            var sub = principal.FindFirst("sub")?.Value
                   ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            return string.IsNullOrWhiteSpace(sub) ? null : sub;
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogWarning("JWT validation failed: token expired. Expiry={Exp} Detail={Detail}",
                ex.Expires.ToString("o"), ex.Message);
            return null;
        }
        catch (SecurityTokenInvalidIssuerException ex)
        {
            string? tokenIss = null;
            try
            {
                var raw = new JwtSecurityTokenHandler().ReadJwtToken(token);
                tokenIss = raw.Issuer;
            }
            catch { /* ignore */ }

            _logger.LogWarning(
                "JWT validation failed: issuer mismatch. ConfiguredIssuer={Expected} TokenIssuer={TokenIss} Detail={Detail}",
                _issuer, tokenIss ?? "<unreadable>", ex.Message);
            return null;
        }
        catch (SecurityTokenSignatureKeyNotFoundException ex)
        {
            _logger.LogWarning("JWT validation failed: signing key not found (rotated?). Detail={Detail}", ex.Message);
            _oidcConfigManager!.RequestRefresh();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT validation failed: {ExType} — {Detail}", ex.GetType().Name, ex.Message);
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
