namespace SmartAssistApi.Services;

/// <summary>
/// Scoped request context populated by <see cref="UserResolutionMiddleware"/>.
/// Inject this instead of calling ClerkAuthService.ExtractUserId manually.
/// </summary>
public interface IAppUserContext
{
    string UserId { get; }
    bool IsAnonymous { get; }
    string Plan { get; }
    DateTime FirstSeenAt { get; }
}

public sealed class AppUserContext : IAppUserContext
{
    public string UserId { get; set; } = "";
    public bool IsAnonymous { get; set; } = true;
    public string Plan { get; set; } = "free";
    public DateTime FirstSeenAt { get; set; }
}
