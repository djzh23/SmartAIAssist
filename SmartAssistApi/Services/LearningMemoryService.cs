using Microsoft.Extensions.Options;
using SmartAssistApi.Data;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Where learning memory is actually stored vs what was configured.</summary>
public readonly record struct LearningMemoryBackendInfo(
    string EffectiveStorage,
    string ConfiguredLearningMemoryStorage,
    bool Degraded,
    string? DegradedReason);

/// <summary>
/// Routes learning memory to Redis or PostgreSQL based on <see cref="DatabaseFeatureOptions"/>.
/// </summary>
public sealed class LearningMemoryService(
    IOptionsSnapshot<DatabaseFeatureOptions> options,
    LearningMemoryRedisService redis,
    IServiceProvider serviceProvider)
{
    private readonly DatabaseFeatureOptions _opts = options.Value;

    private LearningMemoryPostgresService? Postgres =>
        serviceProvider.GetService(typeof(LearningMemoryPostgresService)) as LearningMemoryPostgresService;

    private bool UsePostgres =>
        _opts.PostgresEnabled
        && string.Equals(_opts.LearningMemoryStorage, "postgres", StringComparison.OrdinalIgnoreCase)
        && Postgres is not null;

    /// <summary>Used for response headers and client-visible degraded-mode disclosure.</summary>
    public LearningMemoryBackendInfo GetBackendInfo()
    {
        var configured = string.IsNullOrWhiteSpace(_opts.LearningMemoryStorage)
            ? "redis"
            : _opts.LearningMemoryStorage.Trim();
        var wantsPostgres = _opts.PostgresEnabled
            && string.Equals(configured, "postgres", StringComparison.OrdinalIgnoreCase);
        var effective = UsePostgres ? "postgres" : "redis";
        var degraded = wantsPostgres && !UsePostgres;
        string? reason = null;
        if (degraded)
            reason = Postgres is null ? "no_valid_supabase_connection" : "postgres_unavailable";

        return new LearningMemoryBackendInfo(effective, configured, degraded, reason);
    }

    public Task<UserLearningMemory> GetMemory(string userId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.GetMemory(userId, cancellationToken)
            : redis.GetMemory(userId, cancellationToken);

    public Task AddInsight(string userId, LearningInsight insight, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.AddInsight(userId, insight, cancellationToken)
            : redis.AddInsight(userId, insight, cancellationToken);

    public Task ResolveInsight(string userId, string insightId, CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.ResolveInsight(userId, insightId, cancellationToken)
            : redis.ResolveInsight(userId, insightId, cancellationToken);

    public Task PatchInsight(
        string userId,
        string insightId,
        string? title,
        string? content,
        bool? resolved,
        CancellationToken cancellationToken = default) =>
        UsePostgres
            ? Postgres!.PatchInsight(userId, insightId, title, content, resolved, cancellationToken)
            : redis.PatchInsight(userId, insightId, title, content, resolved, cancellationToken);

    public string BuildInsightsContext(UserLearningMemory memory, string? forJobApplicationId = null)
    {
        var insights = memory.Insights ?? new List<LearningInsight>();
        var q = insights.Where(i => !i.Resolved);
        if (!string.IsNullOrWhiteSpace(forJobApplicationId))
        {
            q = q.Where(i =>
                string.IsNullOrWhiteSpace(i.JobApplicationId)
                || string.Equals(i.JobApplicationId, forJobApplicationId, StringComparison.Ordinal));
        }

        var activeInsights = q
            .OrderBy(i => i.SortOrder)
            .ThenByDescending(i => i.UpdatedAt == default ? i.CreatedAt : i.UpdatedAt)
            .Take(8)
            .ToList();

        if (activeInsights.Count == 0)
            return string.Empty;

        var lines = new List<string> { "[ERKENNTNISSE AUS FRÜHEREN GESPRÄCHEN]" };

        foreach (var insight in activeInsights)
        {
            var prefix = insight.Category switch
            {
                "skill_gap" => "Lücke",
                "strength" => "Stärke",
                "goal" => "Ziel",
                "action_item" => "ToDo",
                _ => "Notiz",
            };
            var label = string.IsNullOrWhiteSpace(insight.Title) ? insight.Content : $"{insight.Title}: {insight.Content}";
            lines.Add($"- {prefix}: {label}");
        }

        lines.Add("[ENDE ERKENNTNISSE]");
        lines.Add("Beziehe dich natürlich auf diese Erkenntnisse wenn relevant. Frage nach Fortschritt bei Lücken und ToDos.");

        return string.Join("\n", lines);
    }
}
