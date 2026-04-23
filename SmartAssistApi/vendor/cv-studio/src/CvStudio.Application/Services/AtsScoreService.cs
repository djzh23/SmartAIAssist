using System.Text;
using System.Text.RegularExpressions;
using CvStudio.Application.Contracts;

namespace CvStudio.Application.Services;

public sealed class AtsScoreService : IAtsScoreService
{
    private static readonly Regex NonWordRegex = new(@"[^a-z0-9äöüß\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceRegex = new("\\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> GermanStopwords =
    [
        "und", "oder", "mit", "für", "von", "in", "an", "auf", "bei",
        "ist", "sind", "wird", "haben", "wir", "sie", "das", "die",
        "der", "ein", "eine", "zu", "im", "dem", "den", "des", "auch",
        "als", "nach", "wenn", "dann", "aber", "noch", "schon", "nur"
    ];

    private static readonly HashSet<string> EnglishStopwords =
    [
        "and", "or", "with", "for", "the", "a", "an", "in", "at", "is",
        "are", "will", "we", "you", "that", "this", "of", "to", "be",
        "have", "has", "was", "were", "from", "by", "on", "as"
    ];

    private static readonly Dictionary<string, string> TechNormalizations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c#"] = "csharp",
        ["c sharp"] = "csharp",
        [".net"] = "dotnet",
        ["asp.net"] = "aspnetcore",
        ["node.js"] = "nodejs",
        ["vue.js"] = "vuejs",
        ["rest-api"] = "restapi",
        ["rest api"] = "restapi",
        ["ci/cd"] = "cicd",
        ["it-support"] = "itsupport",
        ["office365"] = "office365",
        ["microsoft365"] = "microsoft365"
    };

    public AtsScoreResult Calculate(ResumeData resume, string jobDescription)
    {
        return Calculate(resume, jobDescription, JobCategory.Auto);
    }

    public AtsScoreResult Calculate(ResumeData resume, string jobDescription, JobCategory category)
    {
        if (resume is null)
        {
            throw new ArgumentNullException(nameof(resume));
        }

        if (string.IsNullOrWhiteSpace(jobDescription))
        {
            throw new ArgumentException("Job description is required.", nameof(jobDescription));
        }

        var normalizedJobDescription = Normalize(jobDescription);
        var effectiveCategory = category == JobCategory.Auto
            ? DetectCategory(normalizedJobDescription)
            : category;

        var profile = CategoryProfiles.Get(effectiveCategory);
        var catalog = BuildSkillCatalog(effectiveCategory);

        var result = new AtsScoreResult
        {
            DetectedCategory = effectiveCategory
        };

        var jobTokens = Tokenize(normalizedJobDescription)
            .Where(t => t.Length > 2)
            .Where(t => !GermanStopwords.Contains(t) && !EnglishStopwords.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var cvText = GetAllCvText(resume);

        result.MatchedKeywords = jobTokens
            .Where(t => cvText.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();

        result.MissingKeywords = jobTokens
            .Where(t => !cvText.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();
        result.MatchedSkillKeywords = BuildMatchedSkillKeywords(catalog, result.MatchedKeywords);

        var mustHaveMatched = profile.RequiredSkillKeywords
            .Where(k => cvText.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        result.MissingMustHaveKeywords = profile.RequiredSkillKeywords
            .Where(k => !cvText.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        result.HardRequirementsScore = CalcHardRequirementsScore(profile, mustHaveMatched.Count);
        result.KeywordScore = CalcKeywordScore(catalog, result.MatchedKeywords);
        result.EvidenceScore = CalcEvidenceScore(resume, profile, cvText);
        result.CompletenessScore = CalcCompletenessScore(resume);
        result.FormattingScore = CalcFormattingScore(resume, cvText);
        result.LanguageScore = CalcLanguageScore(cvText, profile);

        result.Score = Math.Min(100,
            result.HardRequirementsScore +
            result.KeywordScore +
            result.EvidenceScore +
            result.CompletenessScore +
            result.FormattingScore +
            result.LanguageScore);

        result.Improvements = BuildImprovements(result, resume, profile, effectiveCategory);
        return result;
    }

    private static int CalcHardRequirementsScore(CategoryProfile profile, int matchedCount)
    {
        if (profile.RequiredSkillKeywords.Length == 0)
        {
            return 30;
        }

        var ratio = (double)matchedCount / profile.RequiredSkillKeywords.Length;
        return (int)Math.Min(30, Math.Round(ratio * 30, MidpointRounding.AwayFromZero));
    }

    private static int CalcKeywordScore(IReadOnlyList<SkillDefinition> catalog, IReadOnlyCollection<string> matchedKeywords)
    {
        var matchedSet = matchedKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var totalWeight = catalog.Sum(s => s.Weight);
        if (totalWeight <= 0)
        {
            return 0;
        }

        var scoreWeight = 0.0;
        foreach (var skill in catalog)
        {
            if (skill.Variants.Any(v => matchedSet.Contains(v)))
            {
                scoreWeight += skill.Weight;
            }
        }

        var ratio = scoreWeight / totalWeight;
        return (int)Math.Min(25, Math.Round(ratio * 25, MidpointRounding.AwayFromZero));
    }

    private static List<string> BuildMatchedSkillKeywords(IReadOnlyList<SkillDefinition> catalog, IReadOnlyCollection<string> matchedKeywords)
    {
        var matchedSet = matchedKeywords.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return catalog
            .Where(skill => skill.Variants.Any(v => matchedSet.Contains(v)))
            .Select(skill => skill.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int CalcEvidenceScore(ResumeData resume, CategoryProfile profile, string cvText)
    {
        var score = 0;

        var hasStrongVerbs = profile.StrongVerbs.Any(v => cvText.Contains(v, StringComparison.OrdinalIgnoreCase));
        if (hasStrongVerbs)
        {
            score += 7;
        }

        var hasMetrics = profile.MetricPatterns.Any(p => Regex.IsMatch(cvText, p, RegexOptions.IgnoreCase));
        if (hasMetrics)
        {
            score += 7;
        }

        var hasProjectEvidence = resume.Projects.Count > 0 || resume.WorkItems.Any(w => w.Bullets.Count > 0);
        if (hasProjectEvidence)
        {
            score += 6;
        }

        return Math.Min(20, score);
    }

    private static int CalcCompletenessScore(ResumeData resume)
    {
        var p = resume.Profile;
        var score = 0;

        if (!string.IsNullOrWhiteSpace(p.FirstName)) score += 1;
        if (!string.IsNullOrWhiteSpace(p.LastName)) score += 1;
        if (!string.IsNullOrWhiteSpace(p.Headline)) score += 2;
        if (!string.IsNullOrWhiteSpace(p.Summary) && p.Summary.Length >= 80) score += 2;
        if (!string.IsNullOrWhiteSpace(p.Email)) score += 1;
        if (!string.IsNullOrWhiteSpace(p.Phone)) score += 1;
        if (resume.WorkItems.Count > 0) score += 1;
        if (resume.EducationItems.Count > 0) score += 1;

        return Math.Min(10, score);
    }

    private static int CalcFormattingScore(ResumeData resume, string cvText)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(resume.Profile.Summary) && resume.Profile.Summary.Length >= 100)
        {
            score += 3;
        }

        var hasBullets = resume.WorkItems.Count == 0 || resume.WorkItems.All(w => w.Bullets.Count > 0 || w.Description.Contains('\n'));
        if (hasBullets)
        {
            score += 3;
        }

        var datePattern = @"^(\d{2}/\d{4}|heute|aktuell)$";
        var datesConsistent = resume.WorkItems.Count == 0 || resume.WorkItems.All(w =>
            Regex.IsMatch((w.StartDate ?? string.Empty).Trim(), datePattern, RegexOptions.IgnoreCase) &&
            Regex.IsMatch((w.EndDate ?? string.Empty).Trim(), datePattern, RegexOptions.IgnoreCase));
        if (datesConsistent)
        {
            score += 2;
        }

        var noUmlautErrors = !cvText.Contains(" ae ") && !cvText.Contains(" oe ") && !cvText.Contains(" ue ");
        if (noUmlautErrors)
        {
            score += 2;
        }

        return Math.Min(10, score);
    }

    private static int CalcLanguageScore(string cvText, CategoryProfile profile)
    {
        var score = 0;

        if (profile.StrongVerbs.Any(v => cvText.Contains(v, StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        if (profile.MetricPatterns.Any(p => Regex.IsMatch(cvText, p, RegexOptions.IgnoreCase)))
        {
            score += 2;
        }

        var genericCount = profile.GenericPhrases.Count(p => cvText.Contains(p, StringComparison.OrdinalIgnoreCase));
        if (genericCount <= 1)
        {
            score += 1;
        }

        return Math.Min(5, score);
    }

    private static List<AtsImprovement> BuildImprovements(
        AtsScoreResult result,
        ResumeData resume,
        CategoryProfile profile,
        JobCategory category)
    {
        var improvements = new List<AtsImprovement>();

        if (result.HardRequirementsScore < 18)
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Must-Haves",
                Issue = "Pflicht-Skills sind unvollständig.",
                Suggestion = result.MissingMustHaveKeywords.Count > 0
                    ? $"Ergänze: {string.Join(", ", result.MissingMustHaveKeywords.Take(4))}. {profile.SkillsHint}"
                    : profile.SkillsHint,
                Priority = "Hoch"
            });
        }

        if (result.KeywordScore < 15)
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Keywords",
                Issue = "Zu wenige jobrelevante Begriffe im CV.",
                Suggestion = result.MissingKeywords.Count > 0
                    ? $"Nimm Begriffe auf wie: {string.Join(", ", result.MissingKeywords.Take(5))}."
                    : "Passe die Wortwahl enger auf die Stellenbeschreibung an.",
                Priority = "Mittel"
            });
        }

        var summaryLen = resume.Profile.Summary?.Length ?? 0;
        if (summaryLen < 100)
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Qualifikationsprofil",
                Issue = $"Qualifikationsprofil zu kurz ({summaryLen} Zeichen).",
                Suggestion = profile.SummaryHint,
                Priority = summaryLen < 50 ? "Hoch" : "Mittel"
            });
        }

        if (resume.WorkItems.Count == 0)
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Berufserfahrung",
                Issue = "Keine Berufserfahrung eingetragen.",
                Suggestion = profile.ExperienceHint,
                Priority = "Hoch"
            });
        }
        else if (result.EvidenceScore < 10)
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Belege",
                Issue = "Zu wenige messbare Ergebnisse oder Nachweise.",
                Suggestion = profile.ExperienceHint,
                Priority = "Mittel"
            });
        }

        if (string.IsNullOrWhiteSpace(resume.Profile.ProfileImageUrl))
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Profil",
                Issue = "Kein Profilbild hinterlegt.",
                Suggestion = "Für Bewerbungen in Deutschland kann ein professionelles Foto sinnvoll sein.",
                Priority = "Niedrig"
            });
        }

        if (!improvements.Any(i => i.Priority == "Hoch"))
        {
            improvements.Add(new AtsImprovement
            {
                Category = "Gesamt",
                Issue = "CV ist bereits gut aufgebaut.",
                Suggestion = $"Feinschliff: passe Keywords gezielt auf {CategoryLabel(category)} an.",
                Priority = "Niedrig"
            });
        }

        return improvements
            .OrderBy(i => i.Priority switch
            {
                "Hoch" => 0,
                "Mittel" => 1,
                "Niedrig" => 2,
                _ => 3
            })
            .ToList();
    }

    private static IReadOnlyList<SkillDefinition> BuildSkillCatalog(JobCategory category) => category switch
    {
        JobCategory.SoftwareEntwickler => BuildSoftwareCatalog(),
        JobCategory.ItSupport => BuildSupportCatalog(),
        _ => BuildGeneralCatalog()
    };

    private static List<SkillDefinition> BuildSoftwareCatalog() =>
    [
        new("C#", 4.5, ["csharp"]),
        new(".NET", 4.8, ["dotnet", "aspnetcore", "net core", "net 6", "net 7", "net 8", "net 9"]),
        new("SQL", 4.0, ["sql", "mssql", "mysql", "postgresql", "postgres"]),
        new("REST API", 3.8, ["restapi", "rest", "api", "web api"]),
        new("Entity Framework", 3.5, ["entity framework", "ef core"]),
        new("Blazor", 3.5, ["blazor"]),
        new("Angular", 3.2, ["angular"]),
        new("React", 3.2, ["react"]),
        new("Azure", 3.0, ["azure", "azure devops"]),
        new("Docker", 2.8, ["docker", "container"]),
        new("Git", 2.5, ["git", "github", "gitlab"]),
        new("Clean Architecture", 2.8, ["clean architecture"]),
        new("CI/CD", 2.5, ["cicd", "pipeline", "deployment"])
    ];

    private static List<SkillDefinition> BuildSupportCatalog() =>
    [
        new("Windows", 4.5, ["windows", "windows 10", "windows 11"]),
        new("Active Directory", 4.4, ["active directory", "azure ad", "entra id"]),
        new("Microsoft 365", 4.2, ["microsoft365", "office365", "exchange online"]),
        new("Ticket System", 4.0, ["ticketsystem", "ticket system", "jira", "servicenow"]),
        new("Troubleshooting", 4.0, ["troubleshooting", "fehleranalyse", "störungsanalyse"]),
        new("ITIL", 3.0, ["itil"]),
        new("Network", 3.4, ["tcp ip", "dns", "dhcp", "vpn", "netzwerk"]),
        new("Remote Support", 3.1, ["fernwartung", "remote support", "remotedesktop"]),
        new("Incident", 3.0, ["incident", "incident management"]),
        new("Hardware", 2.8, ["hardware", "rollout", "device"])
    ];

    private static List<SkillDefinition> BuildGeneralCatalog() =>
    [
        new("Kundenbetreuung", 4.0, ["kundenbetreuung", "kundendienst", "service"]),
        new("Teamarbeit", 3.5, ["teamarbeit"]),
        new("Schichtarbeit", 3.2, ["schichtarbeit"]),
        new("Qualitätskontrolle", 3.2, ["qualitätskontrolle", "qualitäts"]),
        new("Hygienestandards", 3.0, ["hygienestandards", "haccp"]),
        new("Kassenarbeit", 3.0, ["kassenarbeit", "kasse", "kassensysteme"]),
        new("Tourenplanung", 3.0, ["tourenplanung", "zustellung"]),
        new("Lagerverwaltung", 3.0, ["lagerverwaltung", "lager", "logistik"]),
        new("Kommissionierung", 3.2, ["kommissionierung", "kommissioniert"])
    ];

    private static JobCategory DetectCategory(string normalizedJobDesc)
    {
        var text = normalizedJobDesc;

        string[] softwareSignals =
        [
            "entwickler", "developer", "software", "programmier",
            "csharp", "dotnet", "java", "python", "react", "angular",
            "blazor", "frontend", "backend", "fullstack", "devops",
            "architektur", "clean architecture", "microservices",
            "api", "datenbank", "repository", "deployment", "git"
        ];

        string[] itSupportSignals =
        [
            "itsupport", "helpdesk", "first level", "second level",
            "ticketsystem", "jira", "servicenow", "itil", "netzwerk",
            "windows", "active directory", "troubleshooting",
            "hardware", "infrastruktur", "support", "incident",
            "fernwartung", "remotedesktop", "patch", "rollout"
        ];

        string[] allgemeinSignals =
        [
            "servicekraft", "küche", "koch", "gastronomie", "gastro",
            "kommissionierung", "zustellung", "logistik", "lager",
            "fahrer", "pakete", "briefe", "post", "sortierung",
            "reinigung", "haushalt", "pflege", "einzelhandel",
            "verkauf", "kasse", "kundendienst", "rezeption"
        ];

        var swScore = softwareSignals.Count(text.Contains);
        var itScore = itSupportSignals.Count(text.Contains);
        var agScore = allgemeinSignals.Count(text.Contains);

        if (swScore == 0 && itScore == 0 && agScore == 0)
            return JobCategory.Allgemein;

        if (swScore >= itScore && swScore >= agScore)
            return JobCategory.SoftwareEntwickler;

        if (itScore >= swScore && itScore >= agScore)
            return JobCategory.ItSupport;

        return JobCategory.Allgemein;
    }

    private static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var text = input.ToLowerInvariant();

        foreach (var (key, value) in TechNormalizations)
            text = text.Replace(key, value, StringComparison.OrdinalIgnoreCase);

        text = NonWordRegex.Replace(text, " ");
        text = MultiSpaceRegex.Replace(text, " ").Trim();

        return text;
    }

    private static string GetAllCvText(ResumeData cv)
    {
        var sb = new StringBuilder();
        var p = cv.Profile;

        sb.Append(p.FirstName).Append(' ')
            .Append(p.LastName).Append(' ')
            .Append(p.Headline).Append(' ')
            .Append(p.Summary).Append(' ')
            .Append(p.Email).Append(' ')
            .Append(p.Phone).Append(' ')
            .Append(p.Location).Append(' ');

        foreach (var work in cv.WorkItems)
        {
            sb.Append(work.Company).Append(' ')
                .Append(work.Role).Append(' ')
                .Append(work.Description).Append(' ')
                .Append(work.StartDate).Append(' ')
                .Append(work.EndDate).Append(' ');
            foreach (var bullet in work.Bullets)
            {
                sb.Append(bullet).Append(' ');
            }
        }

        foreach (var edu in cv.EducationItems)
        {
            sb.Append(edu.School).Append(' ')
                .Append(edu.Degree).Append(' ')
                .Append(edu.StartDate).Append(' ')
                .Append(edu.EndDate).Append(' ');
        }

        foreach (var project in cv.Projects)
        {
            sb.Append(project.Name).Append(' ')
                .Append(project.Description).Append(' ');
            foreach (var tech in project.Technologies)
            {
                sb.Append(tech).Append(' ');
            }
        }

        foreach (var skill in cv.Skills)
        {
            sb.Append(skill.CategoryName).Append(' ');
            foreach (var item in skill.Items)
            {
                sb.Append(item).Append(' ');
            }
        }

        foreach (var hobby in cv.Hobbies)
        {
            sb.Append(hobby).Append(' ');
        }

        return Normalize(sb.ToString());
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        return Normalize(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string CategoryLabel(JobCategory category) => category switch
    {
        JobCategory.SoftwareEntwickler => "Software-Entwicklung",
        JobCategory.ItSupport => "IT-Support",
        _ => "Service / Logistik"
    };

    private sealed record SkillDefinition(string Name, double Weight, IReadOnlyList<string> Variants);
}
