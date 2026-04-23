namespace CvStudio.Application.Contracts;

/// <summary>
/// Logical main-column sections for PDF/DOCX export order (Design A/B main body; Design C main column).
/// </summary>
public static class CvStudioMainSectionOrder
{
    public const string Summary = "summary";
    public const string Skills = "skills";
    public const string Work = "work";
    public const string Education = "education";
    public const string Languages = "languages";
    public const string Interests = "interests";
    public const string Projects = "projects";

    /// <summary>Keys merged from user order + defaults (without interests — inserted after languages).</summary>
    private static readonly string[] DefaultBaseSequence =
    [
        Summary,
        Skills,
        Work,
        Education,
        Languages,
        Projects
    ];

    private static readonly HashSet<string> Known =
    [
        Summary,
        Skills,
        Work,
        Education,
        Languages,
        Interests,
        Projects
    ];

    /// <summary>Returns normalized section keys: user order first, then any defaults not listed.</summary>
    public static IReadOnlyList<string> Resolve(IReadOnlyList<string>? userOrder)
    {
        var result = new List<string>();
        if (userOrder is not null)
        {
            foreach (var raw in userOrder)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var key = raw.Trim().ToLowerInvariant();
                if (!Known.Contains(key))
                    continue;
                if (result.Contains(key, StringComparer.OrdinalIgnoreCase))
                    continue;
                result.Add(key);
            }
        }

        foreach (var d in DefaultBaseSequence)
        {
            if (!result.Contains(d, StringComparer.OrdinalIgnoreCase))
                result.Add(d);
        }

        var langIdx = result.FindIndex(x => x.Equals(Languages, StringComparison.OrdinalIgnoreCase));
        var intIdx = result.FindIndex(x => x.Equals(Interests, StringComparison.OrdinalIgnoreCase));
        if (langIdx >= 0 && intIdx < 0)
            result.Insert(langIdx + 1, Interests);

        return result;
    }
}
