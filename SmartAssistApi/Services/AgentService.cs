using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Options;
using SmartAssistApi.Models;
using SmartAssistApi.Services.Groq;
using SmartAssistApi.Services.Tools;
using Tool = Anthropic.SDK.Common.Tool;

namespace SmartAssistApi.Services;

public class AgentService(
    IConfiguration config,
    ConversationService conversationService,
    SystemPromptBuilder systemPromptBuilder,
    CareerProfileService careerProfileService,
    JobContextExtractor jobExtractor,
    GroqChatCompletionService groqChat,
    IOptions<GroqOptions> groqOptions,
    ILogger<AgentService> logger) : IAgentService
{
    private readonly AnthropicClient _client = new(config["ANTHROPIC_API_KEY"]
        ?? throw new InvalidOperationException("ANTHROPIC_API_KEY missing"));

    public async Task<AgentResponse> RunAsync(AgentRequest request)
    {
        var sessionId = request.SessionId
            ?? throw new ArgumentException("SessionId is required", nameof(request.SessionId));
        var toolType = string.IsNullOrWhiteSpace(request.ToolType)
            ? "general"
            : request.ToolType.ToLowerInvariant();

        var userMessage = UserInputCleaner.CleanUserInput(request.Message);

        var context = await conversationService.GetContextAsync(sessionId, toolType);

        if (toolType == "jobanalyzer"
            && userMessage.Length > 150
            && (context.Job is null || !context.Job.IsAnalyzed))
        {
            var jobContext = await jobExtractor.ExtractAsync(userMessage);
            await conversationService.UpdateContextAsync(sessionId, toolType, ctx => ctx.Job = jobContext);
            context = await conversationService.GetContextAsync(sessionId, toolType);
        }

        if (toolType == "interviewprep"
            && userMessage.Length > 200
            && context.UserCV is null)
        {
            await conversationService.UpdateContextAsync(sessionId, toolType, ctx => ctx.UserCV = userMessage);
            context = await conversationService.GetContextAsync(sessionId, toolType);
        }

        if (toolType == "programming" && LooksLikeCode(userMessage))
        {
            await conversationService.UpdateContextAsync(sessionId, toolType, ctx =>
            {
                ctx.CurrentCodeContext = userMessage[..Math.Min(userMessage.Length, 3000)];
                ctx.ProgrammingLanguage ??= InferProgrammingLanguage(userMessage);
            });
            context = await conversationService.GetContextAsync(sessionId, toolType);
        }

        await UpdateConversationLanguageAsync(sessionId, toolType, userMessage, context);
        context = await conversationService.GetContextAsync(sessionId, toolType);

        await ExtractUserFactsAsync(userMessage, sessionId, toolType);
        context = await conversationService.GetContextAsync(sessionId, toolType);

        var promptParts = systemPromptBuilder.BuildPromptParts(toolType, context, request);
        var profileContext = await BuildCareerProfileContextAsync(request);
        if (!string.IsNullOrEmpty(profileContext))
            promptParts = promptParts.WithProfilePrefix(profileContext);
        LogCachedPrefixEffectiveness(toolType, request.SessionId, promptParts);

        var history = await conversationService.GetHistoryAsync(sessionId, toolType);
        history.Add(new Message(RoleType.User, userMessage));

        var apiMessages = TrimHistoryForLlm(history, toolType, context);

        var tools = BuildTools(toolType, request);
        var maxTokens = MaxTokensFor(toolType);

        if (ShouldTryGroqFirst(toolType, tools)
            && AnthropicMessagesForGroqMapper.TryMap(apiMessages, out var groqMessages))
        {
            var systemCombined = promptParts.ToCombinedPrompt();
            var groqResult = await groqChat.CompleteAsync(systemCombined, groqMessages, maxTokens).ConfigureAwait(false);
            if (groqResult.Success)
            {
                var groqReply = groqResult.Content;
                history.Add(new Message(RoleType.Assistant, groqReply));
                await PostProcessInterviewPrepAsync(sessionId, toolType, groqReply);
                await conversationService.SaveHistoryAsync(sessionId, toolType, history);
                var groqModelLabel = $"groq/{groqResult.Model}";
                return new AgentResponse(
                    groqReply,
                    null,
                    null,
                    groqResult.InputTokens,
                    groqResult.OutputTokens,
                    groqModelLabel,
                    0,
                    0);
            }

            logger.LogWarning(
                "Groq completion failed or empty; falling back to Anthropic. SessionId {SessionId} ToolType {ToolType} Error {Error}",
                sessionId,
                toolType,
                groqResult.Error);
        }

        var parameters = new MessageParameters
        {
            Model = AgentModelSelector.ResolveModel(toolType, config),
            MaxTokens = maxTokens,
            Temperature = 1.0m,
            Messages = apiMessages,
            Tools = tools,
            System = BuildAnthropicSystemMessages(promptParts),
            PromptCaching = PromptCacheType.FineGrained,
        };

        var inputTokens = 0;
        var outputTokens = 0;
        var cacheCreationInputTokens = 0;
        var cacheReadInputTokens = 0;
        var modelUsed = parameters.Model;

        var response = await _client.Messages.GetClaudeMessageAsync(parameters);
        AccumulateTokenUsage(
            response,
            ref inputTokens,
            ref outputTokens,
            ref cacheCreationInputTokens,
            ref cacheReadInputTokens,
            ref modelUsed);

        string finalReply;
        string? toolUsed = null;

        if (response.ToolCalls is { Count: > 0 })
        {
            history.Add(response.Message);

            foreach (var call in response.ToolCalls)
            {
                string result;
                try
                {
                    result = call.Invoke<string>();
                    toolUsed = call.Name;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Tool invocation failed. SessionId {SessionId} ToolType {ToolType} ToolName {ToolName}",
                        sessionId, toolType, call.Name);
                    result = $"Tool execution failed: {ex.Message}";
                }

                history.Add(new Message(call, result));
            }

            parameters.Messages = TrimHistoryForLlm(history, toolType, context);
            var final = await _client.Messages.GetClaudeMessageAsync(parameters);
            AccumulateTokenUsage(
                final,
                ref inputTokens,
                ref outputTokens,
                ref cacheCreationInputTokens,
                ref cacheReadInputTokens,
                ref modelUsed);
            finalReply = final.Message.ToString();
            history.Add(final.Message);
        }
        else
        {
            finalReply = response.Message.ToString();
            history.Add(response.Message);
        }

        await PostProcessInterviewPrepAsync(sessionId, toolType, finalReply);

        await conversationService.SaveHistoryAsync(sessionId, toolType, history);

        return new AgentResponse(
            finalReply,
            toolUsed,
            null,
            inputTokens,
            outputTokens,
            modelUsed,
            cacheCreationInputTokens,
            cacheReadInputTokens);
    }

    /// <summary>
    /// Groq for every tool type when there are no Anthropic tools and history is plain user/assistant text (tool rounds stay on Anthropic).
    /// </summary>
    private bool ShouldTryGroqFirst(string toolType, List<Tool> tools)
    {
        var opt = groqOptions.Value;
        if (!opt.UseAsPrimary || !groqChat.IsConfigured)
            return false;

        if (tools.Count > 0)
            return false;

        return true;
    }

    private async Task PostProcessInterviewPrepAsync(string sessionId, string toolType, string finalReply)
    {
        if (toolType != "interviewprep")
            return;

        var askedQuestion = ExtractInterviewQuestion(finalReply);
        if (string.IsNullOrWhiteSpace(askedQuestion))
            return;

        await conversationService.UpdateContextAsync(sessionId, toolType, ctx =>
        {
            if (!ctx.PractisedQuestions.Contains(askedQuestion, StringComparer.OrdinalIgnoreCase))
                ctx.PractisedQuestions.Add(askedQuestion);
        });
    }

    private async Task<string> BuildCareerProfileContextAsync(AgentRequest request)
    {
        if (request.ProfileToggles is null || string.IsNullOrEmpty(request.CareerProfileUserId))
            return string.Empty;

        var profile = await careerProfileService.GetProfile(request.CareerProfileUserId);
        if (profile is null)
            return string.Empty;

        return careerProfileService.BuildProfileContext(profile, request.ProfileToggles);
    }

    private static int MaxTokensFor(string toolType) => toolType switch
    {
        "general" => 600,
        "language" => 600,
        "jobanalyzer" => 1000,
        "interviewprep" => 1000,
        "programming" => 1200,
        "weather" => 300,
        "jokes" => 300,
        _ => 600,
    };

    /// <summary>
    /// Limits chat messages sent to the LLM; full <paramref name="fullHistory"/> is still persisted.
    /// 6 messages default; 4 when job or interview session context is already loaded (system prompt carries the rest).
    /// </summary>
    private static List<Message> TrimHistoryForLlm(IReadOnlyList<Message> fullHistory, string toolType, SessionContext context)
    {
        var limit = ResolveLlmHistoryMessageLimit(toolType, context);
        if (fullHistory.Count <= limit)
            return fullHistory.ToList();
        return fullHistory.TakeLast(limit).ToList();
    }

    private static int ResolveLlmHistoryMessageLimit(string toolType, SessionContext context)
    {
        var richJob = string.Equals(toolType, "jobanalyzer", StringComparison.OrdinalIgnoreCase)
                      && context.Job is { IsAnalyzed: true };
        var richInterview = string.Equals(toolType, "interviewprep", StringComparison.OrdinalIgnoreCase)
                            && (!string.IsNullOrEmpty(context.InterviewJobTitle)
                                || !string.IsNullOrEmpty(context.UserCV));
        return richJob || richInterview ? 4 : 6;
    }

    private static void AccumulateTokenUsage(
        MessageResponse? resp,
        ref int inputTokens,
        ref int outputTokens,
        ref int cacheCreationInputTokens,
        ref int cacheReadInputTokens,
        ref string? modelUsed)
    {
        if (resp?.Usage is null)
            return;

        inputTokens += resp.Usage.InputTokens;
        outputTokens += resp.Usage.OutputTokens;
        cacheCreationInputTokens += resp.Usage.CacheCreationInputTokens;
        cacheReadInputTokens += resp.Usage.CacheReadInputTokens;
        if (!string.IsNullOrWhiteSpace(resp.Model))
            modelUsed = resp.Model;
    }

    public async IAsyncEnumerable<AgentStreamChunk> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = await RunAsync(request);
        yield return AgentStreamChunk.TextPart(result.Reply);
        yield return AgentStreamChunk.Done(
            result.ToolUsed,
            result.InputTokens,
            result.OutputTokens,
            result.Model,
            result.CacheCreationInputTokens,
            result.CacheReadInputTokens);
    }

    private static List<Tool> BuildTools(string toolType, AgentRequest request) => toolType switch
    {
        "weather" =>
        [
            Tool.FromFunc("get_weather",
                ([FunctionParameter("City name", true)] string city) =>
                    WeatherTool.GetWeatherAsync(city).Result)
        ],
        "jokes" =>
        [
            Tool.FromFunc("get_joke",
                ([FunctionParameter("Topic optional", false)] string topic) =>
                {
                    _ = topic;
                    return JokeTool.GetJokeAsync().Result;
                })
        ],
        // In structured learning mode (---ZIELSPRACHE--- format) Claude must respond
        // directly without calling external tools — the tool would break the format.
        "language" when request.LanguageLearningMode => [],
        "language" =>
        [
            Tool.FromFunc("translate_text",
                ([FunctionParameter("Text", true)] string text,
                 [FunctionParameter("Target language code", true)] string lang) =>
                    TranslationTool.TranslateAsync(text, "auto", lang).Result)
        ],
        _ => []
    };

    /// <summary>
    /// Fine-grained prompt cache: first block is stable per tool (cached); second block is session/turn variable (not cached).
    /// </summary>
    private List<SystemMessage> BuildAnthropicSystemMessages(SystemPromptParts parts)
    {
        var uncached = parts.UncachedSystemBlock;
        if (string.IsNullOrWhiteSpace(uncached))
        {
            throw new InvalidOperationException(
                "Anthropic system prompt: uncached block is empty. Refusing to call the model.");
        }

        return
        [
            new SystemMessage(parts.CachedPrefix, new CacheControl { Type = CacheControlType.ephemeral }),
            new SystemMessage(uncached),
        ];
    }

    private void LogCachedPrefixEffectiveness(string toolType, string sessionId, SystemPromptParts parts)
    {
        var approx = SystemPromptBuilder.ApproximateTokenCount(parts.CachedPrefix);
        if (approx >= SystemPromptBuilder.MinRecommendedCachedPrefixTokens)
            return;

        logger.LogWarning(
            "Degraded prompt caching: cached system prefix is only ~{ApproxTokens} tokens (Anthropic typically needs ~{MinTokens} tokens for an effective cache breakpoint). Expect higher input cost until the prefix grows or the model is changed. SessionId {SessionId} ToolType {ToolType}",
            approx,
            SystemPromptBuilder.MinRecommendedCachedPrefixTokens,
            sessionId,
            toolType);
    }

    private async Task UpdateConversationLanguageAsync(
        string sessionId,
        string toolType,
        string message,
        SessionContext context)
    {
        var detected = ConversationLanguageDetector.DetectLanguage(message);

        await conversationService.UpdateContextAsync(sessionId, toolType, ctx =>
        {
            if (string.IsNullOrWhiteSpace(ctx.ConversationLanguage))
                ctx.ConversationLanguage = "de";

            if (!string.IsNullOrWhiteSpace(detected))
                ctx.ConversationLanguage = detected;
        });

        if (!string.IsNullOrWhiteSpace(detected)
            && !string.Equals(detected, context.ConversationLanguage, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Conversation language switched. SessionId {SessionId} ToolType {ToolType} From {FromLanguage} To {ToLanguage}",
                sessionId,
                toolType,
                context.ConversationLanguage,
                detected);
        }
    }

    private async Task ExtractUserFactsAsync(string message, string sessionId, string toolType)
    {
        var facts = new List<string>();

        var patterns = new[]
        {
            @"(?i)\bmy name is\s+([a-z][a-z\- ]{1,30})",
            @"(?i)\bi am\s+([a-z][a-z\- ]{2,40})",
            @"(?i)\bi have\s+(\d+\+?\s+years?\s+of\s+experience[\w\s\-]*)",
            @"(?i)\bi work as\s+([a-z][a-z\-\s]{2,40})",
            @"(?i)\bi live in\s+([a-z][a-z\-\s]{2,40})",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern);
            if (match.Success)
            {
                var fact = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(fact))
                    facts.Add(fact);
            }
        }

        if (facts.Count == 0)
            return;

        await conversationService.UpdateContextAsync(sessionId, toolType, ctx =>
        {
            foreach (var fact in facts)
            {
                if (!ctx.UserFacts.Contains(fact, StringComparer.OrdinalIgnoreCase))
                    ctx.UserFacts.Add(fact);
            }

            if (ctx.UserFacts.Count > 30)
                ctx.UserFacts = ctx.UserFacts.TakeLast(30).ToList();
        });
    }

    private static bool LooksLikeCode(string text) =>
        text.Contains("```", StringComparison.Ordinal)
        || Regex.IsMatch(text, @"(?m)^\s*(public|private|class|function|def|const|let|var|if\s*\(|for\s*\(|while\s*\()")
        || text.Contains(";", StringComparison.Ordinal);

    private static string InferProgrammingLanguage(string message)
    {
        if (Regex.IsMatch(message, @"\busing\s+System\b|\bnamespace\b|\bpublic\s+class\b", RegexOptions.IgnoreCase)) return "csharp";
        if (Regex.IsMatch(message, @"\bdef\s+\w+\(|\bimport\s+\w+", RegexOptions.IgnoreCase)) return "python";
        if (Regex.IsMatch(message, @"\bfunction\b|\bconst\b|\blet\b|=>", RegexOptions.IgnoreCase)) return "javascript";
        if (Regex.IsMatch(message, @"\bSELECT\b|\bFROM\b|\bWHERE\b", RegexOptions.IgnoreCase)) return "sql";
        return "text";
    }

    private static string? ExtractInterviewQuestion(string reply)
    {
        var m = Regex.Match(reply, @"\[YOUR QUESTION\]\s*:\s*""?([^""\n]+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value.Trim();

        m = Regex.Match(reply, @"\[NEXT QUESTION\]\s*:\s*""?([^""\n]+)", RegexOptions.IgnoreCase);
        if (m.Success)
            return m.Groups[1].Value.Trim();

        m = Regex.Match(reply, @"###\s*Frage\s*1\s*:\s*(.+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }
}
