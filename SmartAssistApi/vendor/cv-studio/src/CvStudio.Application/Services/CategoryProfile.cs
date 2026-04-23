namespace CvStudio.Application.Services;

public sealed class CategoryProfile
{
    public string[] StrongVerbs { get; init; } = [];
    public string[] RequiredSkillKeywords { get; init; } = [];
    public string[] GenericPhrases { get; init; } = [];
    public string[] MetricPatterns { get; init; } = [];
    public string SummaryHint { get; init; } = string.Empty;
    public string SkillsHint { get; init; } = string.Empty;
    public string ExperienceHint { get; init; } = string.Empty;
}

public static class CategoryProfiles
{
    public static CategoryProfile Get(JobCategory category) =>
        category switch
        {
            JobCategory.SoftwareEntwickler => SoftwareProfile,
            JobCategory.ItSupport => ItSupportProfile,
            _ => AllgemeinProfile
        };

    private static readonly CategoryProfile SoftwareProfile = new()
    {
        StrongVerbs =
        [
            "entwickelt", "implementiert", "refactored", "optimiert",
            "designed", "architektiert", "deployed", "integriert",
            "migriert", "automatisiert", "getestet", "dokumentiert",
            "reviewed", "released", "debugged", "built", "shipped"
        ],
        RequiredSkillKeywords =
        [
            "csharp", "dotnet", "java", "python", "javascript",
            "typescript", "react", "angular", "vue", "blazor",
            "sql", "git", "api", "docker", "testing"
        ],
        GenericPhrases =
        [
            "teamfähig", "motiviert", "schnell einarbeitung", "lernbereit"
        ],
        MetricPatterns =
        [
            @"\d+\s*%\s*(schneller|reduziert|verbessert|optimiert)",
            @"\d+\s*(projekte?|features?|bugs?|releases?|sprints?)",
            @"\d+\s*(nutzer|kunden|anfragen|requests)",
            @"\d+\s*ms\b"
        ],
        SummaryHint =
            "Nenne konkrete Technologien: Sprache · Framework · Erfahrungsjahre · Projekttyp (z.B. Backend, Mobile, API).",
        SkillsHint =
            "Strukturiere: Sprachen · Frameworks · Datenbanken · Tools · Cloud. Vermeide zu viele 'Grundkenntnisse'.",
        ExperienceHint =
            "Beschreibe: Was wurde gebaut? Welche Technologien? Welche Ergebnisse? (Performance, Nutzer, Teamgröße)"
    };

    private static readonly CategoryProfile ItSupportProfile = new()
    {
        StrongVerbs =
        [
            "konfiguriert", "installiert", "gewartet", "behoben",
            "dokumentiert", "eskaliert", "unterstützt", "geschult",
            "eingerichtet", "migriert", "rollout", "bereitgestellt",
            "analysiert", "diagnostiziert", "koordiniert"
        ],
        RequiredSkillKeywords =
        [
            "windows", "active directory", "office365", "teams",
            "ticketsystem", "helpdesk", "netzwerk", "vpn",
            "itsupport", "itil", "fernwartung", "hardware"
        ],
        GenericPhrases =
        [
            "kundenorientiert", "dienstleistungsorientiert", "zuverlässig", "teamfähig"
        ],
        MetricPatterns =
        [
            @"\d+\s*(tickets?|anfragen?|incidents?|requests?)",
            @"\d+\s*(nutzer|geräte|pcs?|clients?|standorte)",
            @"\d+\s*%\s*(gelöst|behoben|reduziert)",
            @"(first|second|third)\s*level"
        ],
        SummaryHint =
            "Erwähne: Support-Level (1st/2nd), Systeme die du betreust, Ticketvolumen, Zertifizierungen (ITIL etc.).",
        SkillsHint =
            "Strukturiere: Betriebssysteme · Netzwerk · Tools · Ticketsysteme · Zertifikate. Versionsnummern angeben.",
        ExperienceHint =
            "Nenne: Wie viele Nutzer/Geräte betreut? Welche Systeme? Gelöste Ticketquote? Eskalationsrate reduziert?"
    };

    private static readonly CategoryProfile AllgemeinProfile = new()
    {
        StrongVerbs =
        [
            "betreut", "koordiniert", "sichergestellt", "eingehalten",
            "zugestellt", "kommissioniert", "sortiert", "gewartet",
            "bedient", "abgewickelt", "organisiert", "verantwortlich",
            "eingeplant", "überwacht", "kontrolliert"
        ],
        RequiredSkillKeywords =
        [
            "kundenbetreuung", "teamarbeit", "schichtarbeit",
            "qualitätskontrolle", "hygienestandards", "kassenarbeit",
            "tourenplanung", "lagerverwaltung", "kommissionierung"
        ],
        GenericPhrases =
        [
            "belastbar", "flexibel", "zuverlässig", "pünktlich", "teamfähig"
        ],
        MetricPatterns =
        [
            @"\d+\s*(sendungen|pakete|briefe|artikel|bestellungen)",
            @"\d+\s*(kunden|gäste|tische|bestellungen)",
            @"\d+\s*(kg|tonnen|paletten)",
            @"täglich\s+\d+",
            @"\d+\s*stunden",
            @"\d+\s*€\s*(bargeld|umsatz|kasse)"
        ],
        SummaryHint =
            "Nenne: Branche + Erfahrungsjahre + konkrete Stärke. Beispiel: '3 Jahre Erfahrung in Logistik und Zustellung, zuverlässig bei hohem Sendungsvolumen.'",
        SkillsHint =
            "Nenne branchenspezifische Skills: Gabelstapler-Schein, Führerschein Klasse B, HACCP, Kassensysteme, Sprachen.",
        ExperienceHint =
            "Konkrete Zahlen: Wie viele Pakete/Tag? Wie viele Gäste? Welche Schichten? Welche Geräte/Systeme genutzt?"
    };
}