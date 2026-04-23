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
            UpdatedAtUtc = resume.UpdatedAtUtc
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
