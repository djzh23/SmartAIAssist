namespace SmartAssistApi.Data;

/// <summary>Feature flags for PostgreSQL / Supabase rollout. Defaults keep Redis behavior.</summary>
public sealed class DatabaseFeatureOptions
{
    public const string SectionName = "DatabaseFeatures";

    /// <summary>When true, Supabase/Postgres may be used for features that opt in (e.g. chat notes).</summary>
    public bool PostgresEnabled { get; set; }

    /// <summary><c>redis</c> (default) or <c>postgres</c> for chat notes persistence.</summary>
    public string ChatNotesStorage { get; set; } = "redis";
}
