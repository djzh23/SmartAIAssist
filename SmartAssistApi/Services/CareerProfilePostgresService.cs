using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Career profiles in Supabase/PostgreSQL via EF Core.</summary>
public sealed class CareerProfilePostgresService(SmartAssistDbContext db, ILogger<CareerProfilePostgresService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<CareerProfile?> GetProfile(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        var row = await db.CareerProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return null;

        return DeserializeProfile(row.ProfileJson, userId);
    }

    public async Task SaveProfile(string userId, CareerProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await EnsureAppUserAsync(userId, cancellationToken).ConfigureAwait(false);

        var existing = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);

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

        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, JsonOpts);

        if (existing is null)
        {
            db.CareerProfiles.Add(new CareerProfileEntity
            {
                ClerkUserId = userId,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = now,
                ProfileJson = json,
                CvRawText = null,
                CacheVersion = 1,
            });
        }
        else
        {
            existing.UpdatedAt = now;
            existing.ProfileJson = json;
            existing.CacheVersion = existing.CacheVersion + 1;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetOnboarding(
        string userId,
        string field,
        string fieldLabel,
        string level,
        string levelLabel,
        string? currentRole,
        List<string> goals,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.Field = field;
        profile.FieldLabel = fieldLabel;
        profile.Level = level;
        profile.LevelLabel = levelLabel;
        profile.CurrentRole = currentRole;
        profile.Goals = goals;
        profile.OnboardingCompleted = true;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task SkipOnboardingAsync(string userId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.OnboardingCompleted = true;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCvText(string userId, string cvText, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        var rawForColumn = cvText.Length > CareerProfileStorageLimits.CvRawSeparateKeyMax
            ? cvText[..CareerProfileStorageLimits.CvRawSeparateKeyMax]
            : cvText;
        profile.CvRawText = cvText.Length > CareerProfileStorageLimits.CvRawTextInProfileMax
            ? cvText[..CareerProfileStorageLimits.CvRawTextInProfileMax]
            : cvText;
        profile.CvUploadedAt = DateTime.UtcNow;

        await EnsureAppUserAsync(userId, cancellationToken).ConfigureAwait(false);

        var existing = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(profile, JsonOpts);

        if (existing is null)
        {
            db.CareerProfiles.Add(new CareerProfileEntity
            {
                ClerkUserId = userId,
                CreatedAt = profile.CreatedAt == default ? now : profile.CreatedAt,
                UpdatedAt = now,
                ProfileJson = json,
                CvRawText = rawForColumn,
                CacheVersion = 1,
            });
        }
        else
        {
            existing.UpdatedAt = now;
            existing.ProfileJson = json;
            existing.CvRawText = rawForColumn;
            existing.CacheVersion = existing.CacheVersion + 1;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSkills(string userId, List<string> skills, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.Skills = skills;
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> AddTargetJob(
        string userId,
        string title,
        string? company,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };

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
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
        return job.Id;
    }

    public async Task RemoveTargetJob(string userId, string jobId, CancellationToken cancellationToken = default)
    {
        var profile = await GetProfile(userId, cancellationToken).ConfigureAwait(false)
            ?? new CareerProfile { UserId = userId, CreatedAt = DateTime.UtcNow };
        profile.TargetJobs.RemoveAll(j => j.Id == jobId);
        await SaveProfile(userId, profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task BumpProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return;

        await EnsureAppUserAsync(userId, cancellationToken).ConfigureAwait(false);

        var row = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return;

        row.CacheVersion++;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> GetProfileCacheVersionAsync(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return "0";

        var v = await db.CareerProfiles
            .AsNoTracking()
            .Where(x => x.ClerkUserId == userId)
            .Select(x => x.CacheVersion)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return v.ToString();
    }

    /// <summary>Upserts from Redis backfill; preserves <paramref name="cacheVersion"/> when set.</summary>
    public async Task ImportFromRedisAsync(
        string userId,
        CareerProfile profile,
        string? cvRawFull,
        long? cacheVersion,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await EnsureAppUserAsync(userId, cancellationToken).ConfigureAwait(false);

        profile.UserId = userId;
        profile.CvRawText = Truncate(profile.CvRawText, CareerProfileStorageLimits.CvRawTextInProfileMax);
        profile.CvSummary = Truncate(profile.CvSummary, CareerProfileStorageLimits.CvSummaryMaxChars);
        profile.CvSummaryEn = Truncate(profile.CvSummaryEn, CareerProfileStorageLimits.CvSummaryMaxChars);
        foreach (var job in profile.TargetJobs)
        {
            job.Description = Truncate(job.Description, CareerProfileStorageLimits.TargetJobDescriptionMax);
        }

        var json = JsonSerializer.Serialize(profile, JsonOpts);
        var rawCol = string.IsNullOrEmpty(cvRawFull)
            ? null
            : (cvRawFull.Length > CareerProfileStorageLimits.CvRawSeparateKeyMax
                ? cvRawFull[..CareerProfileStorageLimits.CvRawSeparateKeyMax]
                : cvRawFull);

        var now = DateTime.UtcNow;
        var existing = await db.CareerProfiles
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);

        var version = cacheVersion ?? (existing?.CacheVersion ?? 0);
        if (version < 0)
            version = 0;

        if (existing is null)
        {
            db.CareerProfiles.Add(new CareerProfileEntity
            {
                ClerkUserId = userId,
                CreatedAt = profile.CreatedAt == default ? now : profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt == default ? now : profile.UpdatedAt,
                ProfileJson = json,
                CvRawText = rawCol,
                CacheVersion = version == 0 ? 1 : version,
            });
        }
        else
        {
            existing.UpdatedAt = profile.UpdatedAt == default ? now : profile.UpdatedAt;
            existing.ProfileJson = json;
            existing.CvRawText = rawCol;
            if (cacheVersion.HasValue)
                existing.CacheVersion = cacheVersion.Value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureAppUserAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            if (await db.AppUsers.AsNoTracking().AnyAsync(u => u.ClerkUserId == userId, cancellationToken).ConfigureAwait(false))
                return;

            var now = DateTime.UtcNow;
            db.AppUsers.Add(new AppUserEntity { ClerkUserId = userId, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            if (await db.AppUsers.AsNoTracking().AnyAsync(u => u.ClerkUserId == userId, cancellationToken).ConfigureAwait(false))
                return;
            logger.LogError(ex, "Failed to ensure app_users row for {UserId}", userId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure app_users row for {UserId}", userId);
            throw;
        }
    }

    private static CareerProfile DeserializeProfile(string profileJson, string userId)
    {
        var profile = JsonSerializer.Deserialize<CareerProfile>(profileJson, JsonOpts) ?? new CareerProfile();
        profile.UserId = userId;
        profile.Goals ??= [];
        profile.Skills ??= [];
        profile.Experience ??= [];
        profile.EducationEntries ??= [];
        profile.Languages ??= [];
        profile.TargetJobs ??= [];
        return profile;
    }

    private static string? Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Length > max ? s[..max] : s;
    }
}
