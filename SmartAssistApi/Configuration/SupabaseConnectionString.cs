using Npgsql;

namespace SmartAssistApi.Configuration;

/// <summary>
/// Resolves and normalizes the Supabase / Postgres connection string so Npgsql never receives garbage
/// (BOM, quotes, blank env overrides). Returns null if unset or invalid — callers skip EF registration.
/// </summary>
public static class SupabaseConnectionString
{
    /// <summary>
    /// Priority: <c>DATABASE_URL</c>, <c>SUPABASE__CONNECTIONSTRING</c>, then <c>ConnectionStrings:Supabase</c> (includes <c>ConnectionStrings__Supabase</c> env).
    /// </summary>
    public static string? TryResolve(IConfiguration configuration, out string? rejectReason)
    {
        rejectReason = null;
        var raw =
            Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("SUPABASE__CONNECTIONSTRING")
            ?? configuration.GetConnectionString("Supabase");

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var normalized = Normalize(raw);
        if (string.IsNullOrEmpty(normalized))
        {
            rejectReason = "Supabase connection value was only whitespace.";
            return null;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(normalized);
            return builder.ConnectionString;
        }
        catch (ArgumentException ex)
        {
            rejectReason = $"Supabase connection string is not valid for Npgsql: {ex.Message}";
            return null;
        }
    }

    private static string Normalize(string raw)
    {
        var s = raw.Trim().TrimStart('\ufeff');
        if (s.Length >= 2
            && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s;
    }
}
