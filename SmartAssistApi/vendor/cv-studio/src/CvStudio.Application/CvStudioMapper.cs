using System.Text.Json;
using CvStudio.Application.Contracts;
using CvStudio.Application.DTOs;
using CvStudio.Domain.Entities;

namespace CvStudio.Application;

public static class CvStudioMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string Serialize(ResumeData data) => JsonSerializer.Serialize(data, JsonOptions);

    public static ResumeData Deserialize(string json)
    {
        var data = JsonSerializer.Deserialize<ResumeData>(json, JsonOptions) ?? new ResumeData();
        data.Profile ??= new ProfileData();
        data.Hobbies ??= [];

        // Keep newly introduced optional profile fields normalized for legacy payloads.
        data.Profile.GitHubUrl = NormalizeOptional(data.Profile.GitHubUrl);
        data.Profile.LinkedInUrl = NormalizeOptional(data.Profile.LinkedInUrl);
        data.Profile.PortfolioUrl = NormalizeOptional(data.Profile.PortfolioUrl);
        data.Profile.WorkPermit = NormalizeOptional(data.Profile.WorkPermit);

        data.LanguageItems ??= [];
        NormalizeLegacySectionTitles(data);

        return data;
    }

    public static ResumeDto ToDto(Resume resume)
    {
        return new ResumeDto
        {
            Id = resume.Id,
            Title = resume.Title,
            TemplateKey = resume.TemplateKey,
            ResumeData = Deserialize(resume.CurrentContentJson),
            UpdatedAtUtc = resume.UpdatedAtUtc,
            LinkedJobApplicationId = resume.LinkedJobApplicationId,
            TargetCompany = resume.TargetCompany,
            TargetRole = resume.TargetRole,
            Notes = resume.Notes
        };
    }

    public static ResumeSummaryDto ToSummaryDto(Resume resume)
    {
        ResumeSummaryProfilePreview? preview = null;
        try
        {
            var data = Deserialize(resume.CurrentContentJson);
            if (data.Profile is { } p)
            {
                preview = new ResumeSummaryProfilePreview
                {
                    FirstName = p.FirstName ?? string.Empty,
                    LastName = p.LastName ?? string.Empty,
                    Headline = p.Headline ?? string.Empty,
                    Email = p.Email ?? string.Empty,
                    Location = p.Location ?? string.Empty,
                };
            }
        }
        catch
        {
            // Non-critical — leave preview null rather than failing the list endpoint.
        }

        return new ResumeSummaryDto
        {
            Id = resume.Id,
            Title = resume.Title,
            TemplateKey = resume.TemplateKey,
            UpdatedAtUtc = resume.UpdatedAtUtc,
            LinkedJobApplicationId = resume.LinkedJobApplicationId,
            TargetCompany = resume.TargetCompany,
            TargetRole = resume.TargetRole,
            Notes = resume.Notes,
            ProfilePreview = preview,
        };
    }

    /// <summary>
    /// Maps a <see cref="ResumeSummaryProjection"/> (produced by the optimized list query that
    /// extracts profile fields at the database level) to a <see cref="ResumeSummaryDto"/>.
    /// No JSON deserialization takes place here — all values come pre-extracted from the DB.
    /// </summary>
    public static ResumeSummaryDto ToSummaryDto(ResumeSummaryProjection p)
    {
        ResumeSummaryProfilePreview? preview = null;
        if (!string.IsNullOrEmpty(p.ProfileFirstName) || !string.IsNullOrEmpty(p.ProfileLastName))
        {
            preview = new ResumeSummaryProfilePreview
            {
                FirstName = p.ProfileFirstName ?? string.Empty,
                LastName = p.ProfileLastName ?? string.Empty,
                Headline = p.ProfileHeadline ?? string.Empty,
                Email = p.ProfileEmail ?? string.Empty,
                Location = p.ProfileLocation ?? string.Empty,
            };
        }

        return new ResumeSummaryDto
        {
            Id = p.Id,
            Title = p.Title,
            TemplateKey = p.TemplateKey,
            UpdatedAtUtc = p.UpdatedAtUtc,
            LinkedJobApplicationId = p.LinkedJobApplicationId,
            TargetCompany = p.TargetCompany,
            TargetRole = p.TargetRole,
            Notes = p.Notes,
            ProfilePreview = preview,
        };
    }

    public static ResumeVersionDto ToDto(Snapshot version)
    {
        return new ResumeVersionDto
        {
            Id = version.Id,
            ResumeId = version.ResumeId,
            VersionNumber = version.VersionNumber,
            Label = version.Label,
            ResumeData = Deserialize(version.ContentJson),
            CreatedAtUtc = version.CreatedAtUtc
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    /// <summary>
    /// Legacy: kombinierte Ueberschrift — PDF nutzt nur noch getrennte Sektionen „Sprachen“ / „Interessen“.
    /// </summary>
    private static void NormalizeLegacySectionTitles(ResumeData data)
    {
        var st = data.SectionTitles;
        if (st is null)
            return;
        var combo = st.LanguagesAndInterests?.Trim();
        if (string.IsNullOrEmpty(combo))
            return;
        if (string.IsNullOrWhiteSpace(st.Languages))
            st.Languages = "Sprachen";
        if (string.IsNullOrWhiteSpace(st.Interests))
            st.Interests = "Interessen";
        st.LanguagesAndInterests = null;
    }
}
