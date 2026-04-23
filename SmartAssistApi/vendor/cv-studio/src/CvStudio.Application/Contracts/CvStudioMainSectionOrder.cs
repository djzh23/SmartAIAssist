namespace CvStudio.Application.Contracts;

/// <summary>
/// Logical main-column sections for PDF/DOCX export order (Design A/B main body; Design C main column).
/// Sidebar-only blocks (Design C languages) are not reorderable here and stay fixed.
/// </summary>
public static class CvStudioMainSectionOrder
{
    public const string Summary = "summary";
    public const string Skills = "skills";
    public const string Work = "work";
    public const string Education = "education";
    public const string Languages = "languages";
    public const string Projects = "projects";

    private static readonly string[] DefaultSequence =
    [
        Summary,
        Skills,
        Work,
        Education,
        Languages,
        Projects
    ];

    private static readonly HashSet<string> Known = new(DefaultSequence, StringComparer.OrdinalIgnoreCase);

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

        foreach (var d in DefaultSequence)
        {
            if (!result.Contains(d, StringComparer.OrdinalIgnoreCase))
                result.Add(d);
        }

        return result;
    }
}
