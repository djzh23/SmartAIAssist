using Microsoft.Extensions.Options;
using SmartAssistApi.Data;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Where career profile data is stored vs what was configured.</summary>
public readonly record struct CareerProfileBackendInfo(
    string EffectiveStorage,
    string ConfiguredCareerProfileStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>
/// Routes career profile persistence to Redis or PostgreSQL; prompt-cache TTL keys always use Redis (Upstash).
/// </summary>
public sealed class CareerProfileService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    CareerProfileRedisService redis,
    IServiceProvider serviceProvider)
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private CareerProfilePostgresService? Postgres =>
        serviceProvider.GetService(typeof(CareerProfilePostgresService)) as CareerProfilePostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.CareerProfileStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    public CareerProfileBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.CareerProfileStorage)
            ? "redis"
            : _opts.CareerProfileStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new CareerProfileBackendInfo(effective, configured, degraded, reason);
    }

    public Task<CareerProfile?> GetProfile(string userId) =>
        UsePostgres
            ? Postgres!.GetProfile(userId)
            : redis.GetProfile(userId);

    public Task SaveProfile(string userId, CareerProfile profile) =>
        UsePostgres
            ? Postgres!.SaveProfile(userId, profile)
            : redis.SaveProfile(userId, profile);

    public Task SetOnboarding(
        string userId,
        string field,
        string fieldLabel,
        string level,
        string levelLabel,
        string? currentRole,
        List<string> goals) =>
        UsePostgres
            ? Postgres!.SetOnboarding(userId, field, fieldLabel, level, levelLabel, currentRole, goals)
            : redis.SetOnboarding(userId, field, fieldLabel, level, levelLabel, currentRole, goals);

    public Task SkipOnboardingAsync(string userId) =>
        UsePostgres
            ? Postgres!.SkipOnboardingAsync(userId)
            : redis.SkipOnboardingAsync(userId);

    public Task SetCvText(string userId, string cvText) =>
        UsePostgres
            ? Postgres!.SetCvText(userId, cvText)
            : redis.SetCvText(userId, cvText);

    public Task SetSkills(string userId, List<string> skills) =>
        UsePostgres
            ? Postgres!.SetSkills(userId, skills)
            : redis.SetSkills(userId, skills);

    public Task<string> AddTargetJob(string userId, string title, string? company, string? description) =>
        UsePostgres
            ? Postgres!.AddTargetJob(userId, title, company, description)
            : redis.AddTargetJob(userId, title, company, description);

    public Task RemoveTargetJob(string userId, string jobId) =>
        UsePostgres
            ? Postgres!.RemoveTargetJob(userId, jobId)
            : redis.RemoveTargetJob(userId, jobId);

    public string BuildProfileContext(CareerProfile profile, ProfileContextToggles toggles) =>
        CareerProfileContextBuilder.Build(profile, toggles);

    public async Task BumpProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (UsePostgres)
            await Postgres!.BumpProfileCacheVersionAsync(userId, cancellationToken).ConfigureAwait(false);
        else
            await redis.BumpProfileCacheVersionRedisAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public Task<string> GetProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetProfileCacheVersionAsync(userId, cancellationToken)
            : redis.GetProfileCacheVersionRedisAsync(userId, cancellationToken);

    public Task<string?> TryGetPromptCacheAsync(string key, CancellationToken cancellationToken = default) =>
        redis.TryGetPromptCacheAsync(key, cancellationToken);

    public Task SetPromptCacheAsync(string key, string value, int ttlSeconds, CancellationToken cancellationToken = default) =>
        redis.SetPromptCacheAsync(key, value, ttlSeconds, cancellationToken);
}
