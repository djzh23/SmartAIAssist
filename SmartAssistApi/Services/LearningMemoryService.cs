using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Persists chat-derived insights per user in Redis (Upstash REST). Key: learning:{userId}
/// </summary>
public class LearningMemoryService(
    IConfiguration config,
    HttpClient http,
    ILogger<LearningMemoryService> logger)
{
    private const int MaxInsights = 20;

    private readonly string _restUrl = config["Upstash:RestUrl"] ?? throw new InvalidOperationException("Upstash:RestUrl missing");
    private readonly string _restToken = config["Upstash:RestToken"] ?? throw new InvalidOperationException("Upstash:RestToken missing");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string MemoryKey(string userId) => $"learning:{userId}";

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{_restUrl.TrimEnd('/')}{path}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        return req;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string operation)
    {
        var response = await http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Upstash '{operation}' failed {(int)response.StatusCode}: {body}");
        return body;
    }

    private static UpstashResult? DeserializeUpstash(string body, string operation)
    {
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException($"Upstash '{operation}' empty payload.");
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException($"Upstash '{operation}': {data.Error}");
        return data;
    }

    private static string? FormatResultAsString(object? result)
    {
        if (result is null) return null;
        if (result is JsonElement el)
        {
            if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
            if (el.ValueKind == JsonValueKind.String) return el.GetString();
            return el.ToString();
        }

        return result.ToString();
    }

    private async Task<string?> RedisGetAsync(string key)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/get/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"get:{key}");
        var data = DeserializeUpstash(body, $"get:{key}");
        return FormatResultAsString(data?.Result);
    }

    private async Task RedisSetRawBodyAsync(string key, string value)
    {
        var path = $"/set/{Uri.EscapeDataString(key)}";
        using var req = CreateRequest(HttpMethod.Post, path);
        req.Content = new StringContent(value, Encoding.UTF8, "text/plain");
        _ = await SendAsync(req, $"set-body:{key}");
    }

    public virtual async Task<UserLearningMemory> GetMemory(string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return new UserLearningMemory { UserId = userId };

        try
        {
            var json = await RedisGetAsync(MemoryKey(userId)).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new UserLearningMemory { UserId = userId };

            var mem = JsonSerializer.Deserialize<UserLearningMemory>(json, JsonOpts);
            if (mem is null)
                return new UserLearningMemory { UserId = userId };
            mem.UserId = userId;
            mem.Insights ??= new List<LearningInsight>();
            return mem;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory read failed for user {UserId}", userId);
            return new UserLearningMemory { UserId = userId };
        }
    }

    public virtual async Task AddInsight(string userId, LearningInsight insight, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            var memory = await GetMemory(userId, cancellationToken).ConfigureAwait(false);
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
            var payload = JsonSerializer.Serialize(memory, JsonOpts);
            await RedisSetRawBodyAsync(MemoryKey(userId), payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory write failed for user {UserId}", userId);
        }
    }

    public virtual async Task ResolveInsight(string userId, string insightId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(insightId))
            return;

        try
        {
            var memory = await GetMemory(userId, cancellationToken).ConfigureAwait(false);
            var insight = memory.Insights.FirstOrDefault(i => i.Id == insightId);
            if (insight is null)
                return;

            insight.Resolved = true;
            insight.ResolvedAt = DateTime.UtcNow;
            insight.UpdatedAt = DateTime.UtcNow;
            memory.UpdatedAt = DateTime.UtcNow;
            var payload = JsonSerializer.Serialize(memory, JsonOpts);
            await RedisSetRawBodyAsync(MemoryKey(userId), payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory resolve failed for user {UserId} insight {InsightId}", userId, insightId);
        }
    }

    public string BuildInsightsContext(UserLearningMemory memory, string? forJobApplicationId = null)
    {
        var q = memory.Insights.Where(i => !i.Resolved);
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

    public virtual async Task PatchInsight(
        string userId,
        string insightId,
        string? title,
        string? content,
        bool? resolved,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(insightId))
            return;

        try
        {
            var memory = await GetMemory(userId, cancellationToken).ConfigureAwait(false);
            var insight = memory.Insights.FirstOrDefault(i => i.Id == insightId);
            if (insight is null)
                return;

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
            var payload = JsonSerializer.Serialize(memory, JsonOpts);
            await RedisSetRawBodyAsync(MemoryKey(userId), payload).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning memory patch failed for user {UserId} insight {InsightId}", userId, insightId);
        }
    }

    private sealed record UpstashResult(
        [property: JsonPropertyName("result")] object? Result,
        [property: JsonPropertyName("error")] string? Error);
}
