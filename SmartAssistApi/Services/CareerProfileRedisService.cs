using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Redis (Upstash REST) storage for career profile keys and prompt-cache TTL keys.
/// Keys: <c>profile:{userId}</c>, <c>profile:{userId}:cv_raw</c>, <c>profile_version:{userId}</c>.
/// </summary>
public sealed class CareerProfileRedisService(
    IConfiguration config,
    HttpClient http,
    ILogger<CareerProfileRedisService> logger)
{
    private readonly string _restUrl = RequireUpstashRestUrl(config["Upstash:RestUrl"]);
    private readonly string _restToken = RequireUpstashRestToken(config["Upstash:RestToken"]);

    public static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string ProfileKey(string userId) => $"profile:{userId}";
    private static string CvRawKey(string userId) => $"profile:{userId}:cv_raw";
    private static string ProfileVersionKey(string userId) => $"profile_version:{userId}";

    private static string RequireUpstashRestUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException(
                "Upstash:RestUrl is missing or empty. Set Upstash:RestUrl (or UPSTASH_REDIS_REST_URL) in user secrets, appsettings, or environment.");
        }

        var trimmed = url.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            || (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Upstash:RestUrl must be an absolute http(s) URL (e.g. https://YOUR-REDIS.upstash.io).");
        }

        return trimmed;
    }

    private static string RequireUpstashRestToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Upstash:RestToken is missing or empty. Set Upstash:RestToken (or UPSTASH_REDIS_REST_TOKEN) in user secrets, appsettings, or environment.");
        }

        return token.Trim();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var relative = path.StartsWith('/') ? path : "/" + path;
        var combined = $"{_restUrl.TrimEnd('/')}{relative}";
        if (!Uri.TryCreate(combined, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Could not build Upstash request URL from RestUrl and path '{relative}'.");
        }

        var req = new HttpRequestMessage(method, combined);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        return req;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string operation)
    {
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Upstash operation '{operation}' failed with status {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    private static UpstashResult? DeserializeUpstash(string body, string operation)
    {
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException($"Upstash operation '{operation}' returned empty payload.");

        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException($"Upstash operation '{operation}' failed: {data.Error}");

        return data;
    }

    private async Task<string?> RedisGetAsync(string key)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/get/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"get:{key}");
        var data = DeserializeUpstash(body, $"get:{key}");
        return FormatResultAsString(data?.Result);
    }

    private async Task RedisSetRawBodyAsync(string key, string value)
    {
        var path = $"/set/{Uri.EscapeDataString(key)}";
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = new StringContent(value, Encoding.UTF8, "text/plain");
        _ = await SendAsync(req, $"set-body:{key}");
    }

    private static string? FormatResultAsString(object? result)
    {
        if (result is null)
            return null;

        if (result is JsonElement el)
        {
            if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return null;
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString();
            return el.ToString();
        }

        return result.ToString();
    }

    /// <summary>Full CV blob key (for admin backfill).</summary>
    public Task<string?> GetCvRawAsync(string userId) => RedisGetAsync(CvRawKey(userId));

    /// <summary>Redis profile_version value (ticks string) for admin backfill.</summary>
    public Task<string?> GetProfileVersionRawAsync(string userId) => RedisGetAsync(ProfileVersionKey(userId));

    public async Task<CareerProfile?> GetProfile(string userId)
    {
        var json = await RedisGetAsync(ProfileKey(userId));
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<CareerProfile>(json, JsonOpts);
    }

    public async Task SaveProfile(string userId, CareerProfile profile)
    {
        var existing = await GetProfile(userId);
        profile.UserId = userId;
        profile.UpdatedAt = DateTime.UtcNow;
        if (existing is not null)
        {
            if (profile.CreatedAt == default)
                profile.CreatedAt = existing.CreatedAt;
        }
        else if (profile.CreatedAt == default)
            profile.CreatedAt = DateTime.UtcNow;

        profile.CvRawText = Truncate(profile.CvRawText, CareerProfileStorageLimits.CvRawTextInProfileMax);
        profile.CvSummary = Truncate(profile.CvSummary, CareerProfileStorageLimits.CvSummaryMaxChars);
        profile.CvSummaryEn = Truncate(profile.CvSummaryEn, CareerProfileStorageLimits.CvSummaryMaxChars);
        foreach (var job in profile.TargetJobs)
        {
            job.Description = Truncate(job.Description, CareerProfileStorageLimits.TargetJobDescriptionMax);
        }

        var payload = JsonSerializer.Serialize(profile, JsonOpts);
        await RedisSetRawBodyAsync(ProfileKey(userId), payload);
        try
        {
            await BumpProfileCacheVersionRedisAsync(userId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Profile cache version bump failed after save for user {UserId}", userId);
        }
    }

    public async Task BumpProfileCacheVersionRedisAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = $"/set/{Uri.EscapeDataString(ProfileVersionKey(userId))}";
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = new StringContent(DateTime.UtcNow.Ticks.ToString(), Encoding.UTF8, "text/plain");
        _ = await SendAsync(req, $"set-version:{userId}");
    }

    public async Task<string> GetProfileCacheVersionRedisAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var v = await RedisGetAsync(ProfileVersionKey(userId)).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(v) ? "0" : v.Trim();
    }

    public async Task<string?> TryGetPromptCacheAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await RedisGetAsync(key).ConfigureAwait(false);
    }

    public async Task SetPromptCacheAsync(string key, string value, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await RedisSetRawBodyAsync(key, value).ConfigureAwait(false);
        if (ttlSeconds > 0)
        {
            var expirePath = $"/expire/{Uri.EscapeDataString(key)}/{ttlSeconds}";
            using var req = CreateRequest(HttpMethod.Get, expirePath);
            _ = await SendAsync(req, $"expire:{key}");
        }
    }

    public async Task SetOnboarding(
        string userId,
        string field,
        string fieldLabel,
        string level,
        string levelLabel,
        string? currentRole,
        List<string> goals)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.Field = field;
        profile.FieldLabel = fieldLabel;
        profile.Level = level;
        profile.LevelLabel = levelLabel;
        profile.CurrentRole = currentRole;
        profile.Goals = goals;
        profile.OnboardingCompleted = true;
        await SaveProfile(userId, profile);
    }

    public async Task SkipOnboardingAsync(string userId)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.OnboardingCompleted = true;
        await SaveProfile(userId, profile);
    }

    public async Task SetCvText(string userId, string cvText)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        var rawForKey = cvText.Length > CareerProfileStorageLimits.CvRawSeparateKeyMax
            ? cvText[..CareerProfileStorageLimits.CvRawSeparateKeyMax]
            : cvText;
        profile.CvRawText = cvText.Length > CareerProfileStorageLimits.CvRawTextInProfileMax
            ? cvText[..CareerProfileStorageLimits.CvRawTextInProfileMax]
            : cvText;
        profile.CvUploadedAt = DateTime.UtcNow;
        await RedisSetRawBodyAsync(CvRawKey(userId), rawForKey);
        await SaveProfile(userId, profile);
    }

    public async Task SetSkills(string userId, List<string> skills)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.Skills = skills;
        await SaveProfile(userId, profile);
    }

    public async Task<string> AddTargetJob(string userId, string title, string? company, string? description)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };

        if (profile.TargetJobs.Count >= 3)
            throw new InvalidOperationException("Maximal 3 Wunschstellen erlaubt. Lösche eine bevor du eine neue hinzufügst.");

        var job = new TargetJob
        {
            Title = title,
            Company = company,
            Description = description is { Length: > CareerProfileStorageLimits.TargetJobDescriptionMax }
                ? description[..CareerProfileStorageLimits.TargetJobDescriptionMax]
                : description,
        };
        profile.TargetJobs.Add(job);
        await SaveProfile(userId, profile);
        return job.Id;
    }

    public async Task RemoveTargetJob(string userId, string jobId)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.TargetJobs.RemoveAll(j => j.Id == jobId);
        await SaveProfile(userId, profile);
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length > max ? s[..max] : s;
    }

    private sealed record UpstashResult(
        [property: JsonPropertyName("result")] object? Result,
        [property: JsonPropertyName("error")] string? Error);
}
