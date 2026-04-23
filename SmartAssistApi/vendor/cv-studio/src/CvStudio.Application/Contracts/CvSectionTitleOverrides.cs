using System.ComponentModel.DataAnnotations;

namespace CvStudio.Application.Contracts;

/// <summary>
/// Optionale Ueberschriften fuer CV-Sektionen (PDF/DOCX/Vorschau).
/// Leer lassen = eingebaute Standardsprache (Deutsch).
/// </summary>
public sealed class CvSectionTitleOverrides
{
    [MaxLength(120)]
    public string? QualificationsProfile { get; set; }

    [MaxLength(120)]
    public string? WorkExperience { get; set; }

    [MaxLength(120)]
    public string? Education { get; set; }

    [MaxLength(120)]
    public string? Skills { get; set; }

    [MaxLength(120)]
    public string? Projects { get; set; }

    /// <summary> Design A/B: kombinierter Block Sprachen + Hobbys. </summary>
    [MaxLength(140)]
    public string? LanguagesAndInterests { get; set; }

    /// <summary> Design C: Sidebar Kontakt. </summary>
    [MaxLength(120)]
    public string? Contacts { get; set; }

    /// <summary> Design C: Sidebar Sprachen. </summary>
    [MaxLength(120)]
    public string? Languages { get; set; }

    /// <summary> Design C: Sidebar Hobbys/Interessen. </summary>
    [MaxLength(120)]
    public string? Interests { get; set; }

    /// <summary> Design A/B und DOCX: Label vor der Sprachenliste (z.B. &quot;Langues : &quot;). </summary>
    [MaxLength(80)]
    public string? LanguagesInlineLabel { get; set; }

    /// <summary> Design A/B und DOCX: Label vor den Hobbys (z.B. &quot;Centres d&apos;intérêt : &quot;). </summary>
    [MaxLength(80)]
    public string? InterestsInlineLabel { get; set; }

    /// <summary> Design B: Zeilenkopf vor der Sprachenliste. </summary>
    [MaxLength(80)]
    public string? DesignBLanguagesRowLabel { get; set; }

    /// <summary> Design B: Zeilenkopf vor den Hobbys. </summary>
    [MaxLength(80)]
    public string? DesignBInterestsRowLabel { get; set; }
}

public static class CvSectionTitleResolver
{
    public static string QualificationsProfile(ResumeData data) =>
        Pick(data.SectionTitles?.QualificationsProfile, "Qualifikationsprofil");

    public static string WorkExperience(ResumeData data) =>
        Pick(data.SectionTitles?.WorkExperience, "Berufserfahrung");

    public static string Education(ResumeData data) =>
        Pick(data.SectionTitles?.Education, "Ausbildung");

    public static string Skills(ResumeData data) =>
        Pick(data.SectionTitles?.Skills, "Kenntnisse");

    public static string Projects(ResumeData data) =>
        Pick(data.SectionTitles?.Projects, "Projekte");

    public static string LanguagesAndInterests(ResumeData data) =>
        Pick(data.SectionTitles?.LanguagesAndInterests, "Sprachen & Interessen");

    public static string Contacts(ResumeData data) =>
        Pick(data.SectionTitles?.Contacts, "Kontakte");

    public static string Languages(ResumeData data) =>
        Pick(data.SectionTitles?.Languages, "Sprachen");

    public static string Interests(ResumeData data) =>
        Pick(data.SectionTitles?.Interests, "Interessen");

    public static string LanguagesInlineLabel(ResumeData data) =>
        Pick(data.SectionTitles?.LanguagesInlineLabel, "Sprachen: ");

    public static string InterestsInlineLabel(ResumeData data) =>
        Pick(data.SectionTitles?.InterestsInlineLabel, "Interessen: ");

    public static string DesignBLanguagesRowLabel(ResumeData data) =>
        Pick(data.SectionTitles?.DesignBLanguagesRowLabel, "Sprachen: ");

    public static string DesignBInterestsRowLabel(ResumeData data) =>
        Pick(data.SectionTitles?.DesignBInterestsRowLabel, "Interessen: ");

    private static string Pick(string? user, string fallback) =>
        string.IsNullOrWhiteSpace(user) ? fallback : user.Trim();
}

public static class ResumeDataSectionTitlesExtensions
{
    public static CvSectionTitleOverrides EnsureSectionTitles(this ResumeData data) =>
        data.SectionTitles ??= new CvSectionTitleOverrides();
}
