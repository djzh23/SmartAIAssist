using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace SmartAssistApi.Configuration;

/// <summary>Result of resolving a Supabase / Postgres connection string for Npgsql (no secrets logged).</summary>
public sealed record SupabaseConnectionResolution(
    string? ConnectionString,
    /// <summary>Env or config key that supplied the value (when resolved or when the first invalid value was found).</summary>
    string? SourceKey,
    string? RejectReason);

/// <summary>
/// Resolves and normalizes the Supabase / Postgres connection string so Npgsql never receives garbage
/// (BOM, quotes, blank env overrides). Returns <see cref="SupabaseConnectionResolution.ConnectionString"/> null
/// if unset or invalid — callers skip EF registration.
/// </summary>
public static class SupabaseConnectionString
{
    /// <summary>
    /// Priority: <c>DATABASE_URL</c>, <c>SUPABASE__CONNECTIONSTRING</c>, <c>SUPABASE_CONNECTIONSTRING</c> (single underscore),
    /// then <c>ConnectionStrings:Supabase</c> (includes <c>ConnectionStrings__Supabase</c> env).
    /// </summary>
    public static SupabaseConnectionResolution Resolve(IConfiguration configuration)
    {
        string? whitespaceOnlyKey = null;
        foreach (var (key, raw) in EnumerateCandidates(configuration))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var normalized = Normalize(raw);
            if (string.IsNullOrEmpty(normalized))
            {
                whitespaceOnlyKey ??= key;
                continue;
            }

            try
            {
                var builder = new NpgsqlConnectionStringBuilder(normalized);
                SanitizeConnectionTimeouts(builder);
                TryRewriteSupabaseHostToIpv4(builder);
                return new SupabaseConnectionResolution(builder.ConnectionString, key, null);
            }
            catch (ArgumentException ex)
            {
                return new SupabaseConnectionResolution(null, key, ex.Message);
            }
        }

        if (whitespaceOnlyKey is not null)
        {
            return new SupabaseConnectionResolution(
                null,
                whitespaceOnlyKey,
                "connection value was only whitespace after trim");
        }

        return new SupabaseConnectionResolution(
            null,
            null,
            "no non-empty DATABASE_URL, SUPABASE__CONNECTIONSTRING, SUPABASE_CONNECTIONSTRING, or ConnectionStrings:Supabase");
    }

    private static IEnumerable<(string Key, string? Value)> EnumerateCandidates(IConfiguration configuration)
    {
        yield return ("DATABASE_URL", Environment.GetEnvironmentVariable("DATABASE_URL"));
        yield return ("SUPABASE__CONNECTIONSTRING", Environment.GetEnvironmentVariable("SUPABASE__CONNECTIONSTRING"));
        yield return ("SUPABASE_CONNECTIONSTRING", Environment.GetEnvironmentVariable("SUPABASE_CONNECTIONSTRING"));
        yield return ("ConnectionStrings:Supabase", configuration.GetConnectionString("Supabase"));
    }

    private static string Normalize(string raw)
    {
        var s = raw.Trim().TrimStart('\ufeff');
        if (s.Length >= 2
            && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s;
    }

    /// <summary>
    /// Very large or zero <see cref="NpgsqlConnectionStringBuilder.Timeout"/> values have caused
    /// <see cref="OverflowException"/> inside Npgsql connect (internal TimeSpan math). Clamp to safe bounds.
    /// </summary>
    private static void SanitizeConnectionTimeouts(NpgsqlConnectionStringBuilder builder)
    {
        if (builder.Timeout < 1)
            builder.Timeout = 15;
        else if (builder.Timeout > 600)
            builder.Timeout = 300;

        // Command Timeout: leave 0 (driver default); clamp only absurd positive values.
        if (builder.CommandTimeout > 0 && builder.CommandTimeout > 600)
            builder.CommandTimeout = 300;
    }

    /// <summary>
    /// Hosts like <c>db.*.supabase.co</c> often resolve to IPv6 first; many PaaS egress paths (e.g. Render) cannot
    /// reach that AAAA route (<c>Network is unreachable</c>). Prefer the IPv4 A record for the same hostname.
    /// TLS certs are issued for the hostname, not the literal IP. If the string used <see cref="SslMode.VerifyFull"/> /
    /// <see cref="SslMode.VerifyCA"/>, downgrade to <see cref="SslMode.Require"/> for this path (still encrypted; see Npgsql security docs).
    /// </summary>
    private static void TryRewriteSupabaseHostToIpv4(NpgsqlConnectionStringBuilder builder)
    {
        var host = builder.Host?.Trim() ?? string.Empty;
        if (host.Length == 0 || host.Contains(',', StringComparison.Ordinal))
            return;
        if (!host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
            return;
        if (IPAddress.TryParse(host, out _))
            return;

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            var ipv4 = addresses.FirstOrDefault(static a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null)
                return;

            builder.Host = ipv4.ToString();
            if (builder.SslMode is SslMode.VerifyFull or SslMode.VerifyCA)
                builder.SslMode = SslMode.Require;
        }
        catch
        {
            // Leave original host; connection may still work over IPv6 or after DNS changes.
        }
    }
}
