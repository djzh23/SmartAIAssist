using Microsoft.Extensions.Configuration;

namespace SmartAssistApi.Services;

/// <summary>
/// Admin allow-list: Clerk JWT <c>sub</c> must match an entry from configuration or <c>ADMIN_USER_IDS</c>.
/// Same user identity source as <see cref="ClerkAuthService.ExtractUserId"/> (used by <see cref="Controllers.AdminController"/>).
/// </summary>
public static class AdminAuthorization
{
    public static bool IsUserInAdminList(string? userId, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;

        var configured = configuration["Admin:UserIds"]
            ?? Environment.GetEnvironmentVariable("ADMIN_USER_IDS")
            ?? string.Empty;

        var admins = configured
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return admins.Length > 0 && admins.Contains(userId, StringComparer.Ordinal);
    }
}
