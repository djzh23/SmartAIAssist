using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Token usage metrics in Redis (Upstash REST pipeline).</summary>
public class TokenTrackingRedisService(IConfiguration config, HttpClient http, ILogger<TokenTrackingRedisService> logger)
{
    private readonly string _restUrl = config["Upstash:RestUrl"] ?? throw new InvalidOperationException("Upstash:RestUrl missing");
    private readonly string _restToken = config["Upstash:RestToken"] ?? throw new InvalidOperationException("Upstash:RestToken missing");

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private const int KeyTtlSeconds = 7_776_000; // 90 days

    /// <summary>Non-blocking friendly: swallow errors, log only.</summary>
    public virtual async Task TrackUsageAsync(
        string userId,
        string toolType,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0)
    {
        try
        {
            if (inputTokens < 0 || outputTokens < 0 || cacheCreationInputTokens < 0 || cacheReadInputTokens < 0)
                return;

            var cost = TokenTrackingCostHelper.CalculateCost(model, inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens);
            var inputVolume = inputTokens + cacheCreationInputTokens + cacheReadInputTokens;
            var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var tool = TokenTrackingCostHelper.SanitizeSegment(string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant());
            var modelKey = TokenTrackingCostHelper.SanitizeSegment(string.IsNullOrWhiteSpace(model) ? "unknown" : model);

            var userDay = $"tokens:daily:{userId}:{date}";
            var userModel = $"tokens:daily:{userId}:{date}:model:{modelKey}";
            var userTool = $"tokens:daily:{userId}:{date}:tool:{tool}";
            var globalDay = $"tokens:global:daily:{date}";
            var globalModel = $"tokens:global:daily:{date}:model:{modelKey}";
            var globalTool = $"tokens:global:daily:{date}:tool:{tool}";
            var globalUsers = $"tokens:global:daily:{date}:users";
            var registered = "tokens:users:registered";
            var globalModelsIndex = $"tokens:global:daily:{date}:models";
            var globalToolsIndex = $"tokens:global:daily:{date}:tools";
            var userModelsIndex = $"tokens:daily:{userId}:{date}:models";
            var userToolsIndex = $"tokens:daily:{userId}:{date}:tools";

            var costStr = cost.ToString(CultureInfo.InvariantCulture);

            // Per-tool message counts on the user day hash (tc_{tool}) — one HINCRBY, no read-modify-write race.
            var toolCountField = $"tc_{tool}";

            var pipe = new List<object[]>
            {
                new object[] { "HINCRBY", userDay, "messages", 1 },
                new object[] { "HINCRBY", userDay, "input_tokens", inputVolume },
                new object[] { "HINCRBY", userDay, "output_tokens", outputTokens },
                new object[] { "HINCRBYFLOAT", userDay, "cost_usd", costStr },
                new object[] { "HINCRBY", userDay, toolCountField, 1 },
                new object[] { "HINCRBY", userModel, "messages", 1 },
                new object[] { "HINCRBY", userModel, "input_tokens", inputVolume },
                new object[] { "HINCRBY", userModel, "output_tokens", outputTokens },
                new object[] { "HINCRBYFLOAT", userModel, "cost_usd", costStr },
                new object[] { "HINCRBY", userTool, "messages", 1 },
                new object[] { "HINCRBY", userTool, "input_tokens", inputVolume },
                new object[] { "HINCRBY", userTool, "output_tokens", outputTokens },
                new object[] { "HINCRBYFLOAT", userTool, "cost_usd", costStr },
                new object[] { "HINCRBY", globalDay, "messages", 1 },
                new object[] { "HINCRBY", globalDay, "input_tokens", inputVolume },
                new object[] { "HINCRBY", globalDay, "output_tokens", outputTokens },
                new object[] { "HINCRBYFLOAT", globalDay, "cost_usd", costStr },
                new object[] { "HINCRBY", globalModel, "messages", 1 },
                new object[] { "HINCRBY", globalModel, "input_tokens", inputVolume },
                new object[] { "HINCRBY", globalModel, "output_tokens", outputTokens },
                new object[] { "HINCRBYFLOAT", globalModel, "cost_usd", costStr },
                new object[] { "HINCRBY", globalTool, "messages", 1 },
                new object[] { "HINCRBY", globalTool, "input_tokens", inputVolume },
                new object[] { "HINCRBY", globalTool, "output_tokens", outputTokens },
                new object[] { "HINCRBYFLOAT", globalTool, "cost_usd", costStr },
                new object[] { "SADD", globalUsers, userId },
                new object[] { "SADD", registered, userId },
                new object[] { "SADD", globalModelsIndex, modelKey },
                new object[] { "SADD", globalToolsIndex, tool },
                new object[] { "SADD", userModelsIndex, modelKey },
                new object[] { "SADD", userToolsIndex, tool },
            };

            foreach (var key in new[]
                     {
                         userDay, userModel, userTool, globalDay, globalModel, globalTool, globalUsers, registered,
                         globalModelsIndex, globalToolsIndex, userModelsIndex, userToolsIndex,
                     })
            {
                pipe.Add(new object[] { "EXPIRE", key, KeyTtlSeconds });
            }

            await PipelineAsync(pipe.ToArray(), "TrackUsage").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token tracking failed for user {UserId}", userId);
        }
    }

    public virtual async Task<AdminDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var todayData = await ReadGlobalDayAsync(today, cancellationToken).ConfigureAwait(false);

        var monthCost = 0m;
        var monthMessages = 0;
        for (var d = monthStart; d.Date <= DateTime.UtcNow.Date; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var day = await ReadGlobalDayAsync(ds, cancellationToken).ConfigureAwait(false);
            var dayModels = await ReadModelAggregatesAsync(ds, cancellationToken).ConfigureAwait(false);
            monthCost += dayModels.Values.Sum(m => m.CostUsd);
            monthMessages += day.Messages;
        }

        var last30 = new List<DailyUsage>();
        for (var i = 29; i >= 0; i--)
        {
            var d = DateTime.UtcNow.Date.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var day = await ReadGlobalDayAsync(d, cancellationToken).ConfigureAwait(false);
            var dayModels = await ReadModelAggregatesAsync(d, cancellationToken).ConfigureAwait(false);
            var active = (int)await RedisCardinalityAsync($"tokens:global:daily:{d}:users", cancellationToken).ConfigureAwait(false);
            last30.Add(new DailyUsage
            {
                Date = d,
                Messages = day.Messages,
                InputTokens = day.InputTokens,
                OutputTokens = day.OutputTokens,
                CostUsd = dayModels.Values.Sum(m => m.CostUsd),
                ActiveUsers = active,
            });
        }

        var retentionStart = DateTime.UtcNow.Date.AddDays(-(KeyTtlSeconds / 86400 - 1));
        var top = await GetTopUsersDateRangeAsync(retentionStart, DateTime.UtcNow.Date, 100, cancellationToken).ConfigureAwait(false);
        var rawByModel = await ReadModelAggregatesAsync(today, cancellationToken).ConfigureAwait(false);
        var byModel = TokenTrackingCostHelper.MergeWithConfiguredLlmPlaceholders(rawByModel, config);
        var byTool = await ReadToolAggregatesAsync(today, cancellationToken).ConfigureAwait(false);
        var groqMsgs = byModel.Values.Where(m => string.Equals(m.Provider, "Groq", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Messages);
        var otherMsgs = byModel.Values.Sum(m => m.Messages) - groqMsgs;
        var totalCostTodayLlm = byModel.Values.Sum(m => m.CostUsd);

        var registered = (int)await RedisCardinalityAsync("tokens:users:registered", cancellationToken).ConfigureAwait(false);
        var activeToday = (int)await RedisCardinalityAsync($"tokens:global:daily:{today}:users", cancellationToken).ConfigureAwait(false);

        return new AdminDashboardData
        {
            TotalCostToday = totalCostTodayLlm,
            TotalCostThisMonth = monthCost,
            TotalMessagesToday = todayData.Messages,
            TotalMessagesThisMonth = monthMessages,
            TotalInputTokensToday = todayData.InputTokens,
            TotalOutputTokensToday = todayData.OutputTokens,
            GroqMessagesToday = groqMsgs,
            OtherLlmMessagesToday = otherMsgs,
            ActiveUsersToday = activeToday,
            TotalRegisteredUsers = registered,
            PayingUsers = 0,
            MonthlyRevenue = 0,
            MonthlyProfit = 0,
            TopUsers = top,
            ByModel = byModel,
            ByTool = byTool,
            Last30Days = last30,
            LlmCostPolicyNote =
                "Groq: in SmartAssist mit 0 USD bewertet (kostenloses Kontingent). Anthropic (Haiku/Sonnet) nach konfigurierter Preisliste. " +
                "Die Tabelle unten listet alle konfigurierten LLM-Keys; 0 = heute keine Nutzung.",
        };
    }

    public async Task<UserUsageSummary> GetUserUsageAsync(string userId, string startDate, string endDate, CancellationToken cancellationToken = default)
    {
        if (!TryParseIsoDate(startDate, out var start) || !TryParseIsoDate(endDate, out var end))
            throw new ArgumentException("Invalid date format; use yyyy-MM-dd.");

        var summary = new UserUsageSummary { UserId = userId };
        if (end < start)
            (start, end) = (end, start);

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var day = await ReadUserDayFullAsync(userId, ds, cancellationToken).ConfigureAwait(false);
            summary.TotalMessages += day.Messages;
            summary.TotalInputTokens += day.InputTokens;
            summary.TotalOutputTokens += day.OutputTokens;

            var models = await RedisMembersAsync($"tokens:daily:{userId}:{ds}:models", cancellationToken).ConfigureAwait(false);
            decimal dayLlmCost = 0;
            foreach (var m in models)
            {
                var mu = await ReadHashMetricsAsync($"tokens:daily:{userId}:{ds}:model:{m}", cancellationToken).ConfigureAwait(false);
                dayLlmCost += TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(m, mu.Cost);
                MergeModel(summary.ByModel, m, mu);
            }

            summary.TotalCostUsd += models.Count > 0 ? dayLlmCost : day.CostUsd;

            var tools = await RedisMembersAsync($"tokens:daily:{userId}:{ds}:tools", cancellationToken).ConfigureAwait(false);
            foreach (var t in tools)
            {
                var tu = await ReadHashMetricsAsync($"tokens:daily:{userId}:{ds}:tool:{t}", cancellationToken).ConfigureAwait(false);
                MergeTool(summary.ByTool, t, tu);
            }
        }

        summary.Plan = await RedisGetAsync($"plan:{userId}", cancellationToken).ConfigureAwait(false) ?? "free";
        summary.ByModel = TokenTrackingCostHelper.MergeWithConfiguredLlmPlaceholders(summary.ByModel, config);
        return summary;
    }

    public async Task<List<UserUsageSummary>> GetTopUsersAsync(string date, int limit = 20, CancellationToken cancellationToken = default)
    {
        var userIds = await RedisMembersAsync($"tokens:global:daily:{date}:users", cancellationToken).ConfigureAwait(false);
        var rows = new List<UserUsageSummary>();

        foreach (var uid in userIds)
        {
            var day = await ReadUserDayFullAsync(uid, date, cancellationToken).ConfigureAwait(false);
            if (day.Messages == 0 && day.CostUsd == 0)
                continue;

            var (llmCost, hadModelRows) = await SumUserDayLlmCostUsdAsync(uid, date, cancellationToken).ConfigureAwait(false);
            var displayCost = hadModelRows ? llmCost : day.CostUsd;

            var plan = await RedisGetAsync($"plan:{uid}", cancellationToken).ConfigureAwait(false) ?? "free";
            rows.Add(new UserUsageSummary
            {
                UserId = uid,
                Plan = plan,
                TopTool = day.TopTool,
                TotalMessages = day.Messages,
                TotalInputTokens = day.InputTokens,
                TotalOutputTokens = day.OutputTokens,
                TotalCostUsd = displayCost,
            });
        }

        return rows
            .OrderByDescending(r => r.TotalCostUsd)
            .ThenByDescending(r => r.TotalMessages)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    /// <summary>
    /// Aggregates per-user usage over inclusive UTC dates (e.g. full retention window in Redis).
    /// </summary>
    public async Task<List<UserUsageSummary>> GetTopUsersDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (endUtc < startUtc)
            (startUtc, endUtc) = (endUtc, startUtc);

        var startDate = startUtc.Date;
        var endDate = endUtc.Date;
        var byUser = new Dictionary<string, UserAgg>(StringComparer.Ordinal);

        for (var d = startDate; d <= endDate; d = d.AddDays(1))
        {
            var ds = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var userIds = await RedisMembersAsync($"tokens:global:daily:{ds}:users", cancellationToken).ConfigureAwait(false);
            foreach (var uid in userIds)
            {
                var map = await RedisHGetAllAsync($"tokens:daily:{uid}:{ds}", cancellationToken).ConfigureAwait(false);
                var dayMessages = ParseInt(map, "messages");
                var dayInput = ParseInt(map, "input_tokens");
                var dayOutput = ParseInt(map, "output_tokens");
                var dayCostHash = ParseDecimal(map, "cost_usd");

                var models = await RedisMembersAsync($"tokens:daily:{uid}:{ds}:models", cancellationToken).ConfigureAwait(false);
                decimal llmCost = 0;
                var hadModelRows = models.Count > 0;
                foreach (var m in models)
                {
                    var mu = await ReadHashMetricsAsync($"tokens:daily:{uid}:{ds}:model:{m}", cancellationToken).ConfigureAwait(false);
                    llmCost += TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(m, mu.Cost);
                }

                var displayCost = hadModelRows ? llmCost : dayCostHash;

                if (!byUser.TryGetValue(uid, out var agg))
                {
                    agg = new UserAgg();
                    byUser[uid] = agg;
                }

                agg.Messages += dayMessages;
                agg.InputTokens += dayInput;
                agg.OutputTokens += dayOutput;
                agg.CostUsd += displayCost;
                MergeTcCountsFromMap(agg.ToolMessageCounts, map);
            }
        }

        var rows = new List<UserUsageSummary>();
        foreach (var (uid, agg) in byUser)
        {
            if (agg.Messages == 0 && agg.CostUsd == 0)
                continue;

            var plan = await RedisGetAsync($"plan:{uid}", cancellationToken).ConfigureAwait(false) ?? "free";
            rows.Add(new UserUsageSummary
            {
                UserId = uid,
                Plan = plan,
                TopTool = PickTopToolFromCounts(agg.ToolMessageCounts),
                TotalMessages = agg.Messages,
                TotalInputTokens = agg.InputTokens,
                TotalOutputTokens = agg.OutputTokens,
                TotalCostUsd = agg.CostUsd,
            });
        }

        return rows
            .OrderByDescending(r => r.TotalCostUsd)
            .ThenByDescending(r => r.TotalMessages)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    /// <summary>Parse yyyy-MM-dd and aggregate top users for that inclusive range (clamped to retention and today).</summary>
    public async Task<List<UserUsageSummary>> GetTopUsersForDateRangeQueryAsync(
        string startDate,
        string endDate,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIsoDate(startDate, out var start) || !TryParseIsoDate(endDate, out var end))
            throw new ArgumentException("Invalid date format; use yyyy-MM-dd.");

        var maxSpanDays = KeyTtlSeconds / 86400;
        var minAllowed = DateTime.UtcNow.Date.AddDays(-(maxSpanDays - 1));
        if (start < minAllowed)
            start = minAllowed;
        if (end > DateTime.UtcNow.Date)
            end = DateTime.UtcNow.Date;
        if (end < start)
            (start, end) = (end, start);

        return await GetTopUsersDateRangeAsync(start, end, limit, cancellationToken).ConfigureAwait(false);
    }

    private sealed class UserAgg
    {
        public int Messages;
        public int InputTokens;
        public int OutputTokens;
        public decimal CostUsd;
        public Dictionary<string, int> ToolMessageCounts { get; } = new(StringComparer.Ordinal);
    }

    private static void MergeTcCountsFromMap(Dictionary<string, int> target, Dictionary<string, string> map)
    {
        const string prefix = "tc_";
        foreach (var kv in map)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (!int.TryParse(kv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c))
                continue;
            var toolName = kv.Key.Length > prefix.Length ? kv.Key[prefix.Length..] : "";
            if (string.IsNullOrEmpty(toolName))
                continue;
            target[toolName] = target.GetValueOrDefault(toolName) + c;
        }
    }

    private static string? PickTopToolFromCounts(Dictionary<string, int> counts)
    {
        if (counts.Count == 0)
            return null;
        return counts
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .First().Key;
    }

    public async Task<List<DailyUsage>> GetDailyStatsAsync(int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 366);
        var list = new List<DailyUsage>();
        for (var i = days - 1; i >= 0; i--)
        {
            var d = DateTime.UtcNow.Date.AddDays(-i).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var day = await ReadGlobalDayAsync(d, cancellationToken).ConfigureAwait(false);
            var dayModels = await ReadModelAggregatesAsync(d, cancellationToken).ConfigureAwait(false);
            var active = (int)await RedisCardinalityAsync($"tokens:global:daily:{d}:users", cancellationToken).ConfigureAwait(false);
            list.Add(new DailyUsage
            {
                Date = d,
                Messages = day.Messages,
                InputTokens = day.InputTokens,
                OutputTokens = day.OutputTokens,
                CostUsd = dayModels.Values.Sum(m => m.CostUsd),
                ActiveUsers = active,
            });
        }

        return list;
    }

    private static void MergeModel(Dictionary<string, ModelUsage> dict, string key, (int Messages, int Input, int Output, decimal Cost) m)
    {
        var cost = TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(key, m.Cost);
        if (!dict.TryGetValue(key, out var u))
        {
            dict[key] = new ModelUsage
            {
                Model = key,
                Provider = TokenTrackingCostHelper.InferProviderFromModelKey(key),
                Messages = m.Messages,
                InputTokens = m.Input,
                OutputTokens = m.Output,
                CostUsd = cost,
            };
            return;
        }

        u.Messages += m.Messages;
        u.InputTokens += m.Input;
        u.OutputTokens += m.Output;
        u.CostUsd += cost;
    }

    private static void MergeTool(Dictionary<string, ToolUsage> dict, string key, (int Messages, int Input, int Output, decimal Cost) m)
    {
        if (!dict.TryGetValue(key, out var u))
        {
            dict[key] = new ToolUsage
            {
                Tool = key,
                Messages = m.Messages,
                InputTokens = m.Input,
                OutputTokens = m.Output,
                CostUsd = m.Cost,
            };
            return;
        }

        u.Messages += m.Messages;
        u.InputTokens += m.Input;
        u.OutputTokens += m.Output;
        u.CostUsd += m.Cost;
    }

    private async Task<Dictionary<string, ModelUsage>> ReadModelAggregatesAsync(string date, CancellationToken ct)
    {
        var members = await RedisMembersAsync($"tokens:global:daily:{date}:models", ct).ConfigureAwait(false);
        var dict = new Dictionary<string, ModelUsage>();

        foreach (var name in members)
        {
            var m = await ReadHashMetricsAsync($"tokens:global:daily:{date}:model:{name}", ct).ConfigureAwait(false);
            dict[name] = new ModelUsage
            {
                Model = name,
                Provider = TokenTrackingCostHelper.InferProviderFromModelKey(name),
                Messages = m.Messages,
                InputTokens = m.Input,
                OutputTokens = m.Output,
                CostUsd = TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(name, m.Cost),
            };
        }

        return dict;
    }

    private async Task<Dictionary<string, ToolUsage>> ReadToolAggregatesAsync(string date, CancellationToken ct)
    {
        var members = await RedisMembersAsync($"tokens:global:daily:{date}:tools", ct).ConfigureAwait(false);
        var dict = new Dictionary<string, ToolUsage>();

        foreach (var name in members)
        {
            var m = await ReadHashMetricsAsync($"tokens:global:daily:{date}:tool:{name}", ct).ConfigureAwait(false);
            dict[name] = new ToolUsage
            {
                Tool = name,
                Messages = m.Messages,
                InputTokens = m.Input,
                OutputTokens = m.Output,
                CostUsd = m.Cost,
            };
        }

        return dict;
    }

    private async Task<(int Messages, int InputTokens, int OutputTokens, decimal CostUsd)> ReadGlobalDayAsync(string date, CancellationToken ct) =>
        await ReadHashMetricsAsync($"tokens:global:daily:{date}", ct).ConfigureAwait(false);

    private async Task<(int Messages, int InputTokens, int OutputTokens, decimal CostUsd, string? TopTool)> ReadUserDayFullAsync(string userId, string date, CancellationToken ct)
    {
        var map = await RedisHGetAllAsync($"tokens:daily:{userId}:{date}", ct).ConfigureAwait(false);
        return (
            ParseInt(map, "messages"),
            ParseInt(map, "input_tokens"),
            ParseInt(map, "output_tokens"),
            ParseDecimal(map, "cost_usd"),
            TokenTrackingCostHelper.ParseTopToolFromTcFields(map));
    }

    private async Task<(int Messages, int Input, int Output, decimal Cost)> ReadHashMetricsAsync(string key, CancellationToken ct)
    {
        var map = await RedisHGetAllAsync(key, ct).ConfigureAwait(false);
        return (
            ParseInt(map, "messages"),
            ParseInt(map, "input_tokens"),
            ParseInt(map, "output_tokens"),
            ParseDecimal(map, "cost_usd")
        );
    }

    private static int ParseInt(Dictionary<string, string> map, string field) =>
        map.TryGetValue(field, out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static decimal ParseDecimal(Dictionary<string, string> map, string field) =>
        map.TryGetValue(field, out var s) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static bool TryParseIsoDate(string s, out DateTime utcDate)
    {
        utcDate = default;
        if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return false;
        utcDate = dt.Date;
        return true;
    }

    private async Task<(decimal CostUsd, bool HadPerModelRows)> SumUserDayLlmCostUsdAsync(
        string userId,
        string date,
        CancellationToken ct)
    {
        var models = await RedisMembersAsync($"tokens:daily:{userId}:{date}:models", ct).ConfigureAwait(false);
        if (models.Count == 0)
            return (0m, false);

        decimal s = 0;
        foreach (var m in models)
        {
            var mu = await ReadHashMetricsAsync($"tokens:daily:{userId}:{date}:model:{m}", ct).ConfigureAwait(false);
            s += TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(m, mu.Cost);
        }

        return (s, true);
    }

    private async Task PipelineAsync(object[][] commands, string operation)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_restUrl}/pipeline")
        {
            Content = new StringContent(JsonSerializer.Serialize(commands, JsonOpts), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");

        var body = await SendAsync(req, operation).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Pipeline '{operation}' expected array response.");

        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            {
                var msg = err.GetString();
                if (!string.IsNullOrEmpty(msg))
                    throw new InvalidOperationException($"Redis pipeline error: {msg}");
            }
        }
    }

    private async Task<string> SendAsync(HttpRequestMessage request, string operation)
    {
        var response = await http.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Upstash operation '{operation}' failed with status {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    private async Task<string?> RedisGetAsync(string key, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/get/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"get:{key}").ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts);
        if (!string.IsNullOrWhiteSpace(data?.Error))
            throw new InvalidOperationException(data.Error);
        return data?.Result?.ToString();
    }

    private async Task<long> RedisCardinalityAsync(string key, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/scard/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"scard:{key}").ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException("scard empty payload");
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException(data.Error);
        return ParseLong(data.Result, $"scard:{key}");
    }

    private async Task<HashSet<string>> RedisMembersAsync(string key, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/smembers/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"smembers:{key}").ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException("smembers empty payload");
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException(data.Error);

        var set = new HashSet<string>(StringComparer.Ordinal);
        if (data.Result is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrEmpty(s))
                        set.Add(s);
                }
            }
        }

        return set;
    }

    private async Task<Dictionary<string, string>> RedisHGetAllAsync(string key, CancellationToken ct)
    {
        using var req = CreateRequest(HttpMethod.Get, $"/hgetall/{Uri.EscapeDataString(key)}");
        var body = await SendAsync(req, $"hgetall:{key}").ConfigureAwait(false);
        var data = JsonSerializer.Deserialize<UpstashResult>(body, JsonOpts)
            ?? throw new InvalidOperationException("hgetall empty payload");
        if (!string.IsNullOrWhiteSpace(data.Error))
            throw new InvalidOperationException(data.Error);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (data.Result is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
        {
            var list = arr.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : e.ToString()).ToList();
            for (var i = 0; i + 1 < list.Count; i += 2)
                map[list[i]] = list[i + 1];
        }

        return map;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, $"{_restUrl}{path}");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_restToken}");
        return req;
    }

    private static long ParseLong(object? result, string operation)
    {
        if (result is JsonElement element && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var n))
            return n;
        if (result is JsonElement se && se.ValueKind == JsonValueKind.String && long.TryParse(se.GetString(), out var ns))
            return ns;
        if (result is long l)
            return l;
        if (result is int i)
            return i;
        throw new InvalidOperationException($"Upstash operation '{operation}' returned non-numeric result.");
    }

    private sealed record UpstashResult(
        [property: JsonPropertyName("result")] object? Result,
        [property: JsonPropertyName("error")] string? Error);
}
