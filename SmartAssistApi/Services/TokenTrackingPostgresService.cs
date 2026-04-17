using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Data;
using SmartAssistApi.Data.Entities;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Token usage metrics in PostgreSQL (Supabase).</summary>
public sealed class TokenTrackingPostgresService(
    SmartAssistDbContext db,
    IConfiguration configuration,
    ILogger<TokenTrackingPostgresService> logger)
{
    private const int KeyTtlSeconds = 7_776_000; // 90 days (retention window for range queries)

    public async Task TrackUsageAsync(
        string userId,
        string toolType,
        string model,
        int inputTokens,
        int outputTokens,
        int cacheCreationInputTokens = 0,
        int cacheReadInputTokens = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (inputTokens < 0 || outputTokens < 0 || cacheCreationInputTokens < 0 || cacheReadInputTokens < 0)
                return;

            var cost = TokenTrackingCostHelper.CalculateCost(model, inputTokens, outputTokens, cacheCreationInputTokens, cacheReadInputTokens);
            var inputVolume = inputTokens + cacheCreationInputTokens + cacheReadInputTokens;
            var usageDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            var tool = TokenTrackingCostHelper.SanitizeSegment(string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant());
            var modelKey = TokenTrackingCostHelper.SanitizeSegment(string.IsNullOrWhiteSpace(model) ? "unknown" : model);

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await UpsertAggregateAsync(
                    usageDate,
                    userId,
                    tool,
                    modelKey,
                    inputVolume,
                    outputTokens,
                    cost,
                    cancellationToken).ConfigureAwait(false);

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT INTO token_usage_registered_users (clerk_user_id) VALUES ({userId})
                     ON CONFLICT (clerk_user_id) DO NOTHING
                     """,
                    cancellationToken).ConfigureAwait(false);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token tracking failed for user {UserId}", userId);
        }
    }

    private async Task UpsertAggregateAsync(
        DateOnly usageDate,
        string userId,
        string tool,
        string modelKey,
        int inputVolume,
        int outputTokens,
        decimal cost,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO token_usage_global_daily (usage_date, message_count, input_tokens, output_tokens, cost_usd, updated_at)
             VALUES ({usageDate}, 1, {inputVolume}, {outputTokens}, {cost}, NOW())
             ON CONFLICT (usage_date) DO UPDATE SET
               message_count = token_usage_global_daily.message_count + EXCLUDED.message_count,
               input_tokens = token_usage_global_daily.input_tokens + EXCLUDED.input_tokens,
               output_tokens = token_usage_global_daily.output_tokens + EXCLUDED.output_tokens,
               cost_usd = token_usage_global_daily.cost_usd + EXCLUDED.cost_usd,
               updated_at = NOW()
             """,
            cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO token_usage_daily_user (clerk_user_id, usage_date, message_count, input_tokens, output_tokens, cost_usd, updated_at)
             VALUES ({userId}, {usageDate}, 1, {inputVolume}, {outputTokens}, {cost}, NOW())
             ON CONFLICT (clerk_user_id, usage_date) DO UPDATE SET
               message_count = token_usage_daily_user.message_count + EXCLUDED.message_count,
               input_tokens = token_usage_daily_user.input_tokens + EXCLUDED.input_tokens,
               output_tokens = token_usage_daily_user.output_tokens + EXCLUDED.output_tokens,
               cost_usd = token_usage_daily_user.cost_usd + EXCLUDED.cost_usd,
               updated_at = NOW()
             """,
            cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO token_usage_daily_user_model (clerk_user_id, usage_date, model_key, message_count, input_tokens, output_tokens, cost_usd, updated_at)
             VALUES ({userId}, {usageDate}, {modelKey}, 1, {inputVolume}, {outputTokens}, {cost}, NOW())
             ON CONFLICT (clerk_user_id, usage_date, model_key) DO UPDATE SET
               message_count = token_usage_daily_user_model.message_count + EXCLUDED.message_count,
               input_tokens = token_usage_daily_user_model.input_tokens + EXCLUDED.input_tokens,
               output_tokens = token_usage_daily_user_model.output_tokens + EXCLUDED.output_tokens,
               cost_usd = token_usage_daily_user_model.cost_usd + EXCLUDED.cost_usd,
               updated_at = NOW()
             """,
            cancellationToken).ConfigureAwait(false);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO token_usage_daily_user_tool (clerk_user_id, usage_date, tool, message_count, input_tokens, output_tokens, cost_usd, updated_at)
             VALUES ({userId}, {usageDate}, {tool}, 1, {inputVolume}, {outputTokens}, {cost}, NOW())
             ON CONFLICT (clerk_user_id, usage_date, tool) DO UPDATE SET
               message_count = token_usage_daily_user_tool.message_count + EXCLUDED.message_count,
               input_tokens = token_usage_daily_user_tool.input_tokens + EXCLUDED.input_tokens,
               output_tokens = token_usage_daily_user_tool.output_tokens + EXCLUDED.output_tokens,
               cost_usd = token_usage_daily_user_tool.cost_usd + EXCLUDED.cost_usd,
               updated_at = NOW()
             """,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminDashboardData> GetDashboardDataAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var todayData = await ReadGlobalDayAsync(today, cancellationToken).ConfigureAwait(false);

        var monthCost = 0m;
        var monthMessages = 0;
        for (var d = monthStart.Date; d <= DateTime.UtcNow.Date; d = d.AddDays(1))
        {
            var ds = DateOnly.FromDateTime(d);
            var day = await ReadGlobalDayAsync(ds, cancellationToken).ConfigureAwait(false);
            var dayModels = await ReadModelAggregatesAsync(ds, cancellationToken).ConfigureAwait(false);
            monthCost += dayModels.Values.Sum(m => m.CostUsd);
            monthMessages += day.Messages;
        }

        var last30 = new List<DailyUsage>();
        for (var i = 29; i >= 0; i--)
        {
            var d = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-i));
            var day = await ReadGlobalDayAsync(d, cancellationToken).ConfigureAwait(false);
            var dayModels = await ReadModelAggregatesAsync(d, cancellationToken).ConfigureAwait(false);
            var active = await db.TokenUsageDailyUsers.AsNoTracking()
                .Where(x => x.UsageDate == d && x.MessageCount > 0)
                .Select(x => x.ClerkUserId)
                .Distinct()
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);
            last30.Add(new DailyUsage
            {
                Date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
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
        var byModel = TokenTrackingCostHelper.MergeWithConfiguredLlmPlaceholders(rawByModel, configuration);
        var byTool = await ReadToolAggregatesAsync(today, cancellationToken).ConfigureAwait(false);
        var groqMsgs = byModel.Values.Where(m => string.Equals(m.Provider, "Groq", StringComparison.OrdinalIgnoreCase)).Sum(m => m.Messages);
        var otherMsgs = byModel.Values.Sum(m => m.Messages) - groqMsgs;
        var totalCostTodayLlm = byModel.Values.Sum(m => m.CostUsd);

        var registered = await db.TokenUsageRegisteredUsers.AsNoTracking().CountAsync(cancellationToken).ConfigureAwait(false);
        var activeToday = await db.TokenUsageDailyUsers.AsNoTracking()
            .Where(x => x.UsageDate == today && x.MessageCount > 0)
            .Select(x => x.ClerkUserId)
            .Distinct()
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

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
            var ds = DateOnly.FromDateTime(d);
            var dayRow = await db.TokenUsageDailyUsers.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ClerkUserId == userId && x.UsageDate == ds, cancellationToken)
                .ConfigureAwait(false);
            var dayMessages = dayRow?.MessageCount ?? 0;
            var dayInput = dayRow?.InputTokens ?? 0;
            var dayOutput = dayRow?.OutputTokens ?? 0;
            var dayCostHash = dayRow?.CostUsd ?? 0m;

            summary.TotalMessages += (int)dayMessages;
            summary.TotalInputTokens += (int)dayInput;
            summary.TotalOutputTokens += (int)dayOutput;

            var modelRows = await db.TokenUsageDailyUserModels.AsNoTracking()
                .Where(x => x.ClerkUserId == userId && x.UsageDate == ds)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            decimal dayLlmCost = 0;
            foreach (var row in modelRows)
            {
                var mu = ((int)row.MessageCount, (int)row.InputTokens, (int)row.OutputTokens, row.CostUsd);
                dayLlmCost += TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(row.ModelKey, mu.Item4);
                MergeModel(summary.ByModel, row.ModelKey,
                    (mu.Item1, mu.Item2, mu.Item3, mu.Item4));
            }

            summary.TotalCostUsd += modelRows.Count > 0 ? dayLlmCost : dayCostHash;

            var toolRows = await db.TokenUsageDailyUserTools.AsNoTracking()
                .Where(x => x.ClerkUserId == userId && x.UsageDate == ds)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in toolRows)
            {
                var tu = ((int)row.MessageCount, (int)row.InputTokens, (int)row.OutputTokens, row.CostUsd);
                MergeTool(summary.ByTool, row.Tool, tu);
            }
        }

        summary.Plan = await GetPlanFromDbAsync(userId, cancellationToken).ConfigureAwait(false);
        summary.ByModel = TokenTrackingCostHelper.MergeWithConfiguredLlmPlaceholders(summary.ByModel, configuration);
        return summary;
    }

    private async Task<string> GetPlanFromDbAsync(string userId, CancellationToken cancellationToken)
    {
        if (userId.StartsWith("anon:", StringComparison.Ordinal))
            return "anonymous";
        var row = await db.UserPlans.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClerkUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(row?.Plan) ? "free" : row!.Plan;
    }

    public async Task<List<UserUsageSummary>> GetTopUsersAsync(string date, int limit = 20, CancellationToken cancellationToken = default)
    {
        if (!TryParseIsoDate(date, out var d))
            throw new ArgumentException("Invalid date format; use yyyy-MM-dd.");
        var ds = DateOnly.FromDateTime(d);

        var dayUsers = await db.TokenUsageDailyUsers.AsNoTracking()
            .Where(x => x.UsageDate == ds && (x.MessageCount > 0 || x.CostUsd > 0))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<UserUsageSummary>();
        foreach (var u in dayUsers)
        {
            var (llmCost, hadModelRows) = await SumUserDayLlmCostUsdAsync(u.ClerkUserId, ds, cancellationToken).ConfigureAwait(false);
            var displayCost = hadModelRows ? llmCost : u.CostUsd;

            var plan = await GetPlanFromDbAsync(u.ClerkUserId, cancellationToken).ConfigureAwait(false);
            var topTool = await GetTopToolForUserDayAsync(u.ClerkUserId, ds, cancellationToken).ConfigureAwait(false);
            rows.Add(new UserUsageSummary
            {
                UserId = u.ClerkUserId,
                Plan = plan,
                TopTool = topTool,
                TotalMessages = (int)u.MessageCount,
                TotalInputTokens = (int)u.InputTokens,
                TotalOutputTokens = (int)u.OutputTokens,
                TotalCostUsd = displayCost,
            });
        }

        return rows
            .OrderByDescending(r => r.TotalCostUsd)
            .ThenByDescending(r => r.TotalMessages)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    public async Task<List<UserUsageSummary>> GetTopUsersDateRangeAsync(
        DateTime startUtc,
        DateTime endUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (endUtc < startUtc)
            (startUtc, endUtc) = (endUtc, startUtc);

        var startDate = DateOnly.FromDateTime(startUtc.Date);
        var endDate = DateOnly.FromDateTime(endUtc.Date);

        var agg = await db.TokenUsageDailyUsers.AsNoTracking()
            .Where(x => x.UsageDate >= startDate && x.UsageDate <= endDate)
            .GroupBy(x => x.ClerkUserId)
            .Select(g => new
            {
                UserId = g.Key,
                Messages = g.Sum(x => x.MessageCount),
                InputTokens = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens),
                CostHash = g.Sum(x => x.CostUsd),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<UserUsageSummary>();
        foreach (var a in agg)
        {
            if (a.Messages == 0 && a.CostHash == 0)
                continue;

            var modelCost = await SumUserRangeLlmCostUsdAsync(a.UserId!, startDate, endDate, cancellationToken).ConfigureAwait(false);
            var hadModels = modelCost.HadRows;
            var displayCost = hadModels ? modelCost.CostUsd : a.CostHash;

            var plan = await GetPlanFromDbAsync(a.UserId!, cancellationToken).ConfigureAwait(false);
            var topTool = await GetTopToolForUserRangeAsync(a.UserId!, startDate, endDate, cancellationToken).ConfigureAwait(false);
            rows.Add(new UserUsageSummary
            {
                UserId = a.UserId!,
                Plan = plan,
                TopTool = topTool,
                TotalMessages = (int)a.Messages,
                TotalInputTokens = (int)a.InputTokens,
                TotalOutputTokens = (int)a.OutputTokens,
                TotalCostUsd = displayCost,
            });
        }

        return rows
            .OrderByDescending(r => r.TotalCostUsd)
            .ThenByDescending(r => r.TotalMessages)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private async Task<(decimal CostUsd, bool HadRows)> SumUserRangeLlmCostUsdAsync(
        string userId,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken)
    {
        var rows = await db.TokenUsageDailyUserModels.AsNoTracking()
            .Where(x => x.ClerkUserId == userId && x.UsageDate >= start && x.UsageDate <= end)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (rows.Count == 0)
            return (0m, false);
        decimal s = 0;
        foreach (var r in rows)
            s += TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(r.ModelKey, r.CostUsd);
        return (s, true);
    }

    public Task<List<UserUsageSummary>> GetTopUsersForDateRangeQueryAsync(
        string startDate,
        string endDate,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseIsoDate(startDate, out var start) || !TryParseIsoDate(endDate, out var end))
            throw new ArgumentException("Invalid date format; use yyyy-MM-dd.");

        var maxSpanDays = KeyTtlSeconds / 86400;
        var minAllowed = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-(maxSpanDays - 1)));
        var startD = DateOnly.FromDateTime(start);
        var endD = DateOnly.FromDateTime(end);
        if (startD < minAllowed)
            startD = minAllowed;
        var todayD = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (endD > todayD)
            endD = todayD;
        if (endD < startD)
            (startD, endD) = (endD, startD);

        return GetTopUsersDateRangeAsync(
            DateTime.SpecifyKind(startD.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            DateTime.SpecifyKind(endD.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            limit,
            cancellationToken);
    }

    public async Task<List<DailyUsage>> GetDailyStatsAsync(int days, CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 366);
        var list = new List<DailyUsage>();
        for (var i = days - 1; i >= 0; i--)
        {
            var d = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-i));
            var day = await ReadGlobalDayAsync(d, cancellationToken).ConfigureAwait(false);
            var dayModels = await ReadModelAggregatesAsync(d, cancellationToken).ConfigureAwait(false);
            var active = await db.TokenUsageDailyUsers.AsNoTracking()
                .Where(x => x.UsageDate == d && x.MessageCount > 0)
                .Select(x => x.ClerkUserId)
                .Distinct()
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);
            list.Add(new DailyUsage
            {
                Date = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Messages = day.Messages,
                InputTokens = day.InputTokens,
                OutputTokens = day.OutputTokens,
                CostUsd = dayModels.Values.Sum(m => m.CostUsd),
                ActiveUsers = active,
            });
        }

        return list;
    }

    private async Task<(int Messages, int InputTokens, int OutputTokens, decimal CostUsd)> ReadGlobalDayAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var row = await db.TokenUsageGlobalDaily.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UsageDate == date, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return (0, 0, 0, 0m);
        return ((int)row.MessageCount, (int)row.InputTokens, (int)row.OutputTokens, row.CostUsd);
    }

    private async Task<Dictionary<string, ModelUsage>> ReadModelAggregatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var rows = await db.TokenUsageDailyUserModels.AsNoTracking()
            .Where(x => x.UsageDate == date)
            .GroupBy(x => x.ModelKey)
            .Select(g => new
            {
                ModelKey = g.Key,
                Messages = g.Sum(x => x.MessageCount),
                InputTokens = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens),
                CostUsd = g.Sum(x => x.CostUsd),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dict = new Dictionary<string, ModelUsage>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            dict[r.ModelKey!] = new ModelUsage
            {
                Model = r.ModelKey!,
                Provider = TokenTrackingCostHelper.InferProviderFromModelKey(r.ModelKey!),
                Messages = (int)r.Messages,
                InputTokens = (int)r.InputTokens,
                OutputTokens = (int)r.OutputTokens,
                CostUsd = TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(r.ModelKey!, r.CostUsd),
            };
        }

        return dict;
    }

    private async Task<Dictionary<string, ToolUsage>> ReadToolAggregatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var rows = await db.TokenUsageDailyUserTools.AsNoTracking()
            .Where(x => x.UsageDate == date)
            .GroupBy(x => x.Tool)
            .Select(g => new
            {
                Tool = g.Key,
                Messages = g.Sum(x => x.MessageCount),
                InputTokens = g.Sum(x => x.InputTokens),
                OutputTokens = g.Sum(x => x.OutputTokens),
                CostUsd = g.Sum(x => x.CostUsd),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dict = new Dictionary<string, ToolUsage>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            dict[r.Tool!] = new ToolUsage
            {
                Tool = r.Tool!,
                Messages = (int)r.Messages,
                InputTokens = (int)r.InputTokens,
                OutputTokens = (int)r.OutputTokens,
                CostUsd = r.CostUsd,
            };
        }

        return dict;
    }

    private async Task<(decimal CostUsd, bool HadPerModelRows)> SumUserDayLlmCostUsdAsync(
        string userId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var models = await db.TokenUsageDailyUserModels.AsNoTracking()
            .Where(x => x.ClerkUserId == userId && x.UsageDate == date)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (models.Count == 0)
            return (0m, false);
        decimal s = 0;
        foreach (var m in models)
            s += TokenTrackingCostHelper.AdjustStoredCostUsdForDisplay(m.ModelKey, m.CostUsd);
        return (s, true);
    }

    private async Task<string?> GetTopToolForUserDayAsync(string userId, DateOnly ds, CancellationToken cancellationToken)
    {
        var row = await db.TokenUsageDailyUserTools.AsNoTracking()
            .Where(x => x.ClerkUserId == userId && x.UsageDate == ds)
            .OrderByDescending(x => x.MessageCount)
            .ThenBy(x => x.Tool)
            .Select(x => x.Tool)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row;
    }

    private async Task<string?> GetTopToolForUserRangeAsync(string userId, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var q = await db.TokenUsageDailyUserTools.AsNoTracking()
            .Where(x => x.ClerkUserId == userId && x.UsageDate >= start && x.UsageDate <= end)
            .GroupBy(x => x.Tool)
            .Select(g => new { Tool = g.Key, C = g.Sum(x => x.MessageCount) })
            .OrderByDescending(x => x.C)
            .ThenBy(x => x.Tool)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return q?.Tool;
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

    private static bool TryParseIsoDate(string s, out DateTime utcDate)
    {
        utcDate = default;
        if (!DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            return false;
        utcDate = dt.Date;
        return true;
    }
}
