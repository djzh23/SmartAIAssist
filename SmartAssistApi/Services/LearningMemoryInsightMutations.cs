using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Shared in-memory transforms for learning insights (Redis and Postgres backends).</summary>
internal static class LearningMemoryInsightMutations
{
    internal const int MaxInsights = 20;

    internal static void AddInsight(UserLearningMemory memory, LearningInsight insight, string userId)
    {
        memory.Insights ??= new List<LearningInsight>();
        var isDuplicate = memory.Insights.Any(i =>
            i.Category == insight.Category
            && i.Content.Equals(insight.Content, StringComparison.OrdinalIgnoreCase)
            && string.Equals(i.JobApplicationId ?? string.Empty, insight.JobApplicationId ?? string.Empty, StringComparison.Ordinal));
        if (isDuplicate)
            return;

        insight.UpdatedAt = DateTime.UtcNow;
        memory.Insights.Add(insight);

        if (memory.Insights.Count > MaxInsights)
        {
            var toRemove = memory.Insights
                .OrderBy(i => i.Resolved ? 0 : 1)
                .ThenBy(i => i.CreatedAt)
                .First();
            memory.Insights.Remove(toRemove);
        }

        memory.UpdatedAt = DateTime.UtcNow;
        memory.UserId = userId;
    }

    internal static bool TryResolveInsight(UserLearningMemory memory, string insightId, string userId)
    {
        memory.Insights ??= new List<LearningInsight>();
        var insight = memory.Insights.FirstOrDefault(i => i.Id == insightId);
        if (insight is null)
            return false;

        insight.Resolved = true;
        insight.ResolvedAt = DateTime.UtcNow;
        insight.UpdatedAt = DateTime.UtcNow;
        memory.UpdatedAt = DateTime.UtcNow;
        memory.UserId = userId;
        return true;
    }

    internal static bool TryPatchInsight(
        UserLearningMemory memory,
        string insightId,
        string? title,
        string? content,
        bool? resolved,
        string userId)
    {
        memory.Insights ??= new List<LearningInsight>();
        var insight = memory.Insights.FirstOrDefault(i => i.Id == insightId);
        if (insight is null)
            return false;

        if (title is not null)
            insight.Title = title;
        if (content is not null)
            insight.Content = content;
        if (resolved is true)
        {
            insight.Resolved = true;
            insight.ResolvedAt = DateTime.UtcNow;
        }
        else if (resolved is false)
        {
            insight.Resolved = false;
            insight.ResolvedAt = null;
        }

        insight.UpdatedAt = DateTime.UtcNow;
        memory.UpdatedAt = DateTime.UtcNow;
        memory.UserId = userId;
        return true;
    }
}
