using System.Linq;
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
                PrepareHostAndSsl(builder);
                SanitizeConnectionTimeouts(builder);
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
        // NpgsqlConnector splits the remaining connect timeout across resolved endpoints; keep Timeout in a
        // finite, reasonable range so per-endpoint TimeSpan math cannot overflow.
        if (builder.Timeout < 1 || builder.Timeout > 600)
            builder.Timeout = 30;
        else if (builder.Timeout > 300)
            builder.Timeout = 300;

        // Command Timeout: leave 0 (driver default); clamp only absurd positive values.
        if (builder.CommandTimeout > 0 && builder.CommandTimeout > 600)
            builder.CommandTimeout = 300;
    }

    /// <summary>
    /// For <c>*.supabase.co</c> hosts, attempts to prefer the IPv4 A record so that PaaS environments
    /// without IPv6 egress (e.g. Render) can reach Supabase. DNS is resolved once at startup as a
    /// best-effort optimisation only — if DNS fails or returns no addresses the original host name is
    /// kept and Npgsql resolves it again at actual connection time. If we connect by IP, downgrade
    /// <see cref="SslMode.VerifyFull"/> / <see cref="SslMode.VerifyCA"/> to <see cref="SslMode.Require"/>.
    /// </summary>
    private static void PrepareHostAndSsl(NpgsqlConnectionStringBuilder builder)
    {
        var host = builder.Host?.Trim() ?? string.Empty;
        if (host.Length == 0 || host.Contains(',', StringComparison.Ordinal))
            return;

        // Npgsql uses Unix-domain sockets when Host looks like a filesystem path; do not DNS those.
        if (host.StartsWith("/", StringComparison.Ordinal) || host.StartsWith("\\", StringComparison.Ordinal))
            return;

        if (IPAddress.TryParse(host, out _))
            return;

        // Only attempt IPv4 substitution for Supabase direct-DB hosts.
        if (!host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase))
            return;

        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch
        {
            // DNS unavailable at startup (e.g. Render network not yet ready). Keep the hostname and
            // let Npgsql resolve it at connect time.
            return;
        }

        if (addresses.Length == 0)
        {
            // DNS returned nothing at startup — keep the hostname; Npgsql retries at connect time.
            return;
        }

        var ipv4 = addresses.FirstOrDefault(static a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is null)
            return;

        builder.Host = ipv4.ToString();
        if (builder.SslMode is SslMode.VerifyFull or SslMode.VerifyCA)
            builder.SslMode = SslMode.Require;
    }
}
