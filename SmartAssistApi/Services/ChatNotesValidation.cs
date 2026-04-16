namespace SmartAssistApi.Services;

/// <summary>Shared limits and normalization for Redis and Postgres chat-notes implementations.</summary>
public static class ChatNotesValidation
{
    public const int MaxNotesPerUser = 400;
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 100_000;
    public const int MaxTagLength = 48;
    public const int MaxTagsPerNote = 24;

    public static (string Title, string Body, List<string> Tags) Normalize(string title, string body, IReadOnlyList<string> tags)
        => (NormalizeTitle(title), NormalizeBody(body), NormalizeTags(tags));

    public static string NormalizeTitle(string title)
    {
        var t = title.Trim();
        if (t.Length > MaxTitleLength)
            t = t[..MaxTitleLength];
        return t;
    }

    public static string NormalizeBody(string body)
    {
        var b = body.Trim();
        if (b.Length > MaxBodyLength)
            b = b[..MaxBodyLength];
        return b;
    }

    public static List<string> NormalizeTags(IReadOnlyList<string> tags)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags)
        {
            if (set.Count >= MaxTagsPerNote)
                break;
            var t = raw.Trim().ToLowerInvariant();
            if (t.Length > MaxTagLength)
                t = t[..MaxTagLength];
            if (t.Length > 0)
                set.Add(t);
        }

        return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }
}
