using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Verwaltet Karriereprofile in Redis (Upstash REST).
/// Redis-Key: profile:{userId} → JSON-String des CareerProfile (ohne TTL).
/// </summary>
public class CareerProfileService(
    IConfiguration config,
    HttpClient http,
    ILogger<CareerProfileService> logger)
{
    private readonly string _restUrl = RequireUpstashRestUrl(config["Upstash:RestUrl"]);
    private readonly string _restToken = RequireUpstashRestToken(config["Upstash:RestToken"]);

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>~300 Tokens @ 4 Zeichen/Token</summary>
    private const int MaxProfileContextChars = 1200;

    private const int CvRawTextInProfileMax = 2000;
    private const int CvRawSeparateKeyMax = 50_000;
    private const int TargetJobDescriptionMax = 2000;

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
        if (!Uri.TryCreate(combined, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Could not build Upstash request URL from RestUrl and path '{relative}'.");
        }

        var req = new HttpRequestMessage(method, uri);
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

    /// <summary>SET mit Request-Body (für große JSON-Werte; URL-/set/… ist zu kurz).</summary>
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
            if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined)
                return null;
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString();
            return el.ToString();
        }

        return result.ToString();
    }

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

        profile.CvRawText = Truncate(profile.CvRawText, CvRawTextInProfileMax);
        foreach (var job in profile.TargetJobs)
        {
            job.Description = Truncate(job.Description, TargetJobDescriptionMax);
        }

        var payload = JsonSerializer.Serialize(profile, JsonOpts);
        await RedisSetRawBodyAsync(ProfileKey(userId), payload);
        try
        {
            await BumpProfileCacheVersionAsync(userId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Profile cache version bump failed after save for user {UserId}", userId);
        }
    }

    /// <summary>Versions-Stempel für Prompt-Cache-Invalidierung (TTL-basierte Keys hängen davon ab).</summary>
    public async Task BumpProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = $"/set/{Uri.EscapeDataString(ProfileVersionKey(userId))}";
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = new StringContent(DateTime.UtcNow.Ticks.ToString(), Encoding.UTF8, "text/plain");
        _ = await SendAsync(req, $"set-version:{userId}");
    }

    public async Task<string> GetProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
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

    /// <summary>Onboarding überspringen — Profil als abgeschlossen markieren ohne Pflichtfelder.</summary>
    public async Task SkipOnboardingAsync(string userId)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.OnboardingCompleted = true;
        await SaveProfile(userId, profile);
    }

    public async Task SetCvText(string userId, string cvText)
    {
        var profile = await GetProfile(userId) ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        var rawForKey = cvText.Length > CvRawSeparateKeyMax ? cvText[..CvRawSeparateKeyMax] : cvText;
        profile.CvRawText = cvText.Length > CvRawTextInProfileMax ? cvText[..CvRawTextInProfileMax] : cvText;
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
            Description = description is { Length: > TargetJobDescriptionMax } ? description[..TargetJobDescriptionMax] : description,
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

    public string BuildProfileContext(CareerProfile profile, ProfileContextToggles toggles)
    {
        var parts = new List<string> { "[NUTZERPROFIL]" };

        if (toggles.IncludeBasicProfile)
        {
            if (!string.IsNullOrEmpty(profile.FieldLabel))
                parts.Add($"Berufsfeld: {profile.FieldLabel}");
            if (!string.IsNullOrEmpty(profile.LevelLabel))
                parts.Add($"Erfahrung: {profile.LevelLabel}");
            if (!string.IsNullOrEmpty(profile.CurrentRole))
                parts.Add($"Aktuelle Rolle: {profile.CurrentRole}");
            if (profile.Goals.Count > 0)
                parts.Add($"Ziel: {string.Join(", ", profile.Goals)}");
        }

        if (toggles.IncludeSkills && profile.Skills.Count > 0)
            parts.Add($"Skills: {string.Join(", ", profile.Skills)}");

        if (toggles.IncludeExperience && profile.Experience.Count > 0)
        {
            foreach (var exp in profile.Experience.Take(3))
            {
                var line = $"Erfahrung: {exp.Title}";
                if (!string.IsNullOrEmpty(exp.Company)) line += $" bei {exp.Company}";
                if (!string.IsNullOrEmpty(exp.Duration)) line += $" ({exp.Duration})";
                parts.Add(line);
            }
        }

        if (toggles.IncludeCv)
        {
            if (!string.IsNullOrEmpty(profile.CvSummary))
                parts.Add($"CV-Zusammenfassung: {profile.CvSummary}");
            else if (!string.IsNullOrEmpty(profile.CvRawText))
            {
                var n = Math.Min(500, profile.CvRawText.Length);
                parts.Add($"CV-Auszug: {profile.CvRawText[..n]}");
            }
        }

        if (!string.IsNullOrEmpty(toggles.ActiveTargetJobId))
        {
            var targetJob = profile.TargetJobs.FirstOrDefault(j => j.Id == toggles.ActiveTargetJobId);
            if (targetJob is not null)
            {
                var line = $"Zielstelle: {targetJob.Title}";
                if (!string.IsNullOrEmpty(targetJob.Company)) line += $" bei {targetJob.Company}";
                parts.Add(line);
                if (!string.IsNullOrEmpty(targetJob.Description))
                {
                    var n = Math.Min(500, targetJob.Description.Length);
                    parts.Add($"Stellenanforderungen: {targetJob.Description[..n]}");
                }
            }
        }

        parts.Add("[ENDE NUTZERPROFIL]");

        if (parts.Count <= 2)
            return string.Empty;

        var text = string.Join("\n", parts);
        if (text.Length > MaxProfileContextChars)
            text = text[..MaxProfileContextChars] + "\n[…]";

        return text;
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
