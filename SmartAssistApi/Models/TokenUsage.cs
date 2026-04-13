namespace SmartAssistApi.Models;

public class TokenUsage
{
    public string UserId { get; set; } = string.Empty;
    public string ToolType { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class UserUsageSummary
{
    public string UserId { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string Plan { get; set; } = string.Empty;
    /// <summary>Tool with the most messages for the period (from tc_* counters on user daily hashes).</summary>
    public string? TopTool { get; set; }
    public int TotalMessages { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public Dictionary<string, ModelUsage> ByModel { get; set; } = new();
    public Dictionary<string, ToolUsage> ByTool { get; set; } = new();
}

public class ModelUsage
{
    public string Model { get; set; } = string.Empty;
    /// <summary>Groq vs Anthropic (from Redis model key after sanitization).</summary>
    public string Provider { get; set; } = "Anthropic";
    public int Messages { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public class ToolUsage
{
    public string Tool { get; set; } = string.Empty;
    public int Messages { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public class AdminDashboardData
{
    public decimal TotalCostToday { get; set; }
    public decimal TotalCostThisMonth { get; set; }
    public int TotalMessagesToday { get; set; }
    public int TotalMessagesThisMonth { get; set; }
    public int TotalInputTokensToday { get; set; }
    public int TotalOutputTokensToday { get; set; }
    /// <summary>Messages counted against Groq model keys today (primary path).</summary>
    public int GroqMessagesToday { get; set; }
    /// <summary>Messages counted against non-Groq models today (Anthropic Haiku/Sonnet fallback and tool flows).</summary>
    public int OtherLlmMessagesToday { get; set; }
    public int ActiveUsersToday { get; set; }
    public int TotalRegisteredUsers { get; set; }
    public int PayingUsers { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public decimal MonthlyProfit { get; set; }
    public List<UserUsageSummary> TopUsers { get; set; } = new();
    public Dictionary<string, ModelUsage> ByModel { get; set; } = new();
    public Dictionary<string, ToolUsage> ByTool { get; set; } = new();
    public List<DailyUsage> Last30Days { get; set; } = new();
    /// <summary>Shown on admin UI: how LLM rows are priced (Groq = 0 USD in-app).</summary>
    public string LlmCostPolicyNote { get; set; } = "";
}

public class DailyUsage
{
    public string Date { get; set; } = string.Empty;
    public int Messages { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public int ActiveUsers { get; set; }
}
