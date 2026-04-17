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
/// (BOM, quotes, blank env overrides, postgresql:// URIs). Returns <see cref="SupabaseConnectionResolution.ConnectionString"/> null
/// if unset or invalid — callers skip EF registration.
/// </summary>
public static class SupabaseConnectionString
{
    /// <summary>
    /// Priority: <c>DATABASE_URL</c>, <c>SUPABASE__CONNECTIONSTRING</c>, <c>SUPABASE_CONNECTIONSTRING</c> (single underscore),
    /// then <c>ConnectionStrings:Supabase</c> (includes <c>ConnectionStrings__Supabase</c> env).
    /// Accepts both postgresql:// URI format and Npgsql key=value format.
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
                var builder = IsUri(normalized)
                    ? BuilderFromUri(normalized)
                    : new NpgsqlConnectionStringBuilder(normalized);

                PrepareHostAndSsl(builder);
                SanitizeConnectionTimeouts(builder);
                return new SupabaseConnectionResolution(builder.ConnectionString, key, null);
            }
            catch (ArgumentException ex)
            {
                return new SupabaseConnectionResolution(null, key, ex.Message);
            }
            catch (UriFormatException ex)
            {
                return new SupabaseConnectionResolution(null, key, $"Invalid URI format: {ex.Message}");
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

    private static bool IsUri(string s) =>
        s.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a <c>postgresql://user:pass@host:port/db?key=value</c> URI into an
    /// <see cref="NpgsqlConnectionStringBuilder"/>. <c>NpgsqlConnectionStringBuilder</c> only understands
    /// the key=value wire format; passing a URI to its constructor throws <see cref="ArgumentException"/>.
    /// </summary>
    private static NpgsqlConnectionStringBuilder BuilderFromUri(string uri)
    {
        var u = new Uri(uri);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host     = u.Host,
            Port     = u.Port > 0 ? u.Port : 5432,
            Database = u.AbsolutePath.TrimStart('/'),
        };

        if (!string.IsNullOrEmpty(u.UserInfo))
        {
            var idx = u.UserInfo.IndexOf(':');
            if (idx < 0)
            {
                builder.Username = Uri.UnescapeDataString(u.UserInfo);
            }
            else
            {
                builder.Username = Uri.UnescapeDataString(u.UserInfo[..idx]);
                builder.Password = Uri.UnescapeDataString(u.UserInfo[(idx + 1)..]);
            }
        }

        // Forward any query-string parameters (e.g. ?sslmode=require) to the builder.
        var query = u.Query.TrimStart('?');
        if (!string.IsNullOrEmpty(query))
        {
            foreach (var param in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = param.IndexOf('=');
                if (eq <= 0) continue;
                var k = Uri.UnescapeDataString(param[..eq]);
                var v = Uri.UnescapeDataString(param[(eq + 1)..]);
                try { builder[k] = v; } catch { /* ignore unknown keys */ }
            }
        }

        return builder;
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
        if (builder.Timeout < 1 || builder.Timeout > 600)
            builder.Timeout = 30;
        else if (builder.Timeout > 300)
            builder.Timeout = 300;

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
            return;
        }

        if (addresses.Length == 0)
            return;

        var ipv4 = addresses.FirstOrDefault(static a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is null)
            return;

        builder.Host = ipv4.ToString();
        if (builder.SslMode is SslMode.VerifyFull or SslMode.VerifyCA)
            builder.SslMode = SslMode.Require;
    }
}
