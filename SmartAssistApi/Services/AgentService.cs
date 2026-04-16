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
    PromptComposer promptComposer,
    JobContextExtractor jobExtractor,
    GroqChatCompletionService groqChat,
    LearningMemoryService learningMemoryService,
    IOptions<GroqOptions> groqOptions,
    ILogger<AgentService> logger) : IAgentService
{
    private readonly AnthropicClient _client = new(config["ANTHROPIC_API_KEY"]
        ?? throw new InvalidOperationException("ANTHROPIC_API_KEY missing"));

    public async Task<AgentResponse> RunAsync(AgentRequest request)
    {
        var sessionId = request.SessionId
            ?? throw new ArgumentException("SessionId is required", nameof(request.SessionId));
        var scopeUserId = string.IsNullOrWhiteSpace(request.ConversationScopeUserId)
            ? throw new InvalidOperationException("ConversationScopeUserId must be set by the API layer.")
            : request.ConversationScopeUserId;
        var toolType = string.IsNullOrWhiteSpace(request.ToolType)
            ? "general"
            : request.ToolType.ToLowerInvariant();

        var primaryUserMessage = UserInputCleaner.CleanUserInput(request.Message);

        var context = await conversationService.GetContextAsync(scopeUserId, sessionId, toolType);

        await UpdateConversationLanguageAsync(scopeUserId, sessionId, toolType, primaryUserMessage, context);
        await ApplyCareerToolSetupFromRequestAsync(scopeUserId, sessionId, toolType, request).ConfigureAwait(false);
        context = await conversationService.GetContextAsync(scopeUserId, sessionId, toolType);

        var legacyJobExtract = toolType == "jobanalyzer"
                                 && !CareerTurnAssembler.HasStructuredSetup(request)
                                 && primaryUserMessage.Length > 150
                                 && (context.Job is null || !context.Job.IsAnalyzed);
        if (legacyJobExtract)
        {
            var jobContext = await jobExtractor.ExtractAsync(primaryUserMessage).ConfigureAwait(false);
            await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx => ctx.Job = jobContext)
                .ConfigureAwait(false);
            context = await conversationService.GetContextAsync(scopeUserId, sessionId, toolType);
        }

        var legacyInterviewCv = toolType == "interviewprep"
                                && !CareerTurnAssembler.HasStructuredSetup(request)
                                && primaryUserMessage.Length > 200
                                && context.UserCV is null;
        if (legacyInterviewCv)
        {
            await conversationService
                .UpdateContextAsync(scopeUserId, sessionId, toolType, ctx => ctx.UserCV = primaryUserMessage)
                .ConfigureAwait(false);
            context = await conversationService.GetContextAsync(scopeUserId, sessionId, toolType);
        }

        if (toolType == "programming" && LooksLikeCode(primaryUserMessage))
        {
            await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx =>
            {
                ctx.CurrentCodeContext = primaryUserMessage[..Math.Min(primaryUserMessage.Length, 3000)];
                ctx.ProgrammingLanguage ??= InferProgrammingLanguage(primaryUserMessage);
            });
            context = await conversationService.GetContextAsync(scopeUserId, sessionId, toolType);
        }

        var factsMerged = await ExtractUserFactsAsync(primaryUserMessage, scopeUserId, sessionId, toolType)
            .ConfigureAwait(false);
        if (factsMerged)
            context = await conversationService.GetContextAsync(scopeUserId, sessionId, toolType);

        var userTurnMessage = CareerTurnAssembler.ComposeEffectiveUserMessage(request, toolType, primaryUserMessage);

        var promptParts = await promptComposer.ComposePromptPartsAsync(request.CareerProfileUserId, request, context)
            .ConfigureAwait(false);
        var promptWithSummary = string.IsNullOrWhiteSpace(context.ConversationSummary)
            ? promptParts
            : promptParts.WithConversationSummary(context.ConversationSummary);
        LogCachedPrefixEffectiveness(toolType, request.SessionId, promptWithSummary);

        var history = await conversationService.GetHistoryAsync(scopeUserId, sessionId, toolType);
        history.Add(new Message(RoleType.User, userTurnMessage));

        var apiMessages = LlmHistoryTrimmer.Trim(history, toolType, context);

        var tools = BuildTools(toolType, request);
        var maxTokens = GroqInferenceParameters.MaxTokensFor(toolType);

        if (ShouldTryGroqFirst(toolType, tools)
            && AnthropicMessagesForGroqMapper.TryMap(apiMessages, out var groqMessages))
        {
            var systemCombined = promptWithSummary.ToCombinedPrompt();
            var sampling = GroqInferenceParameters.SamplingFor(toolType);
            var groqResult = await groqChat
                .CompleteAsync(systemCombined, groqMessages, maxTokens, sampling)
                .ConfigureAwait(false);
            if (groqResult.Success)
            {
                var groqReply = groqResult.Content ?? string.Empty;
                groqReply = await TryRepairGroqReplyIfNeededAsync(groqReply, toolType).ConfigureAwait(false);
                if (!ShouldRejectGroqReply(groqReply, toolType))
                {
                    history.Add(new Message(RoleType.Assistant, groqReply));
                    await PostProcessInterviewPrepAsync(scopeUserId, sessionId, toolType, groqReply);
                    await conversationService.SaveHistoryAsync(scopeUserId, sessionId, toolType, history);
                    await AppendConversationSummaryAsync(scopeUserId, sessionId, toolType, primaryUserMessage, groqReply)
                        .ConfigureAwait(false);
                    QueueInsightExtraction(scopeUserId, toolType, primaryUserMessage, groqReply, request.JobApplicationId);
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
                    "Groq reply rejected by quality gate; falling back to Anthropic. SessionId {SessionId} ToolType {ToolType}",
                    sessionId,
                    toolType);
            }
            else
            {
                logger.LogWarning(
                    "Groq completion failed or empty; falling back to Anthropic. SessionId {SessionId} ToolType {ToolType} Error {Error}",
                    sessionId,
                    toolType,
                    groqResult.Error);
            }
        }

        var parameters = new MessageParameters
        {
            Model = AgentModelSelector.ResolveModel(toolType, config),
            MaxTokens = maxTokens,
            Temperature = GroqInferenceParameters.AnthropicTemperatureFor(toolType),
            Messages = apiMessages,
            Tools = tools,
            System = BuildAnthropicSystemMessages(promptWithSummary),
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

            parameters.Messages = LlmHistoryTrimmer.Trim(history, toolType, context);
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

        await PostProcessInterviewPrepAsync(scopeUserId, sessionId, toolType, finalReply);

        await conversationService.SaveHistoryAsync(scopeUserId, sessionId, toolType, history);

        await AppendConversationSummaryAsync(scopeUserId, sessionId, toolType, primaryUserMessage, finalReply)
            .ConfigureAwait(false);

        QueueInsightExtraction(scopeUserId, toolType, primaryUserMessage, finalReply, request.JobApplicationId);

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

    private void QueueInsightExtraction(
        string scopeUserId,
        string toolType,
        string userMessage,
        string fullResponse,
        string? jobApplicationId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndSaveInsightsAsync(
                        scopeUserId,
                        toolType,
                        userMessage,
                        fullResponse,
                        jobApplicationId,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Insight extraction task failed for user {UserId}", scopeUserId);
            }
        });
    }

    private async Task ExtractAndSaveInsightsAsync(
        string userId,
        string toolType,
        string userMessage,
        string fullResponse,
        string? jobApplicationId,
        CancellationToken cancellationToken)
    {
        if (toolType is not ("jobanalyzer" or "interviewprep"))
            return;

        var insights = new List<LearningInsight>();
        var responseLower = fullResponse.ToLowerInvariant();

        var gapPatterns = new[] { "✗", "fehlt", "fehlt komplett", "nicht vorhanden", "lücke", "mangel" };
        foreach (var pattern in gapPatterns)
        {
            var index = responseLower.IndexOf(pattern, StringComparison.Ordinal);
            if (index < 0)
                continue;

            var start = Math.Max(0, index - 50);
            var end = Math.Min(fullResponse.Length, index + pattern.Length + 50);
            var context = fullResponse[start..end].Trim();
            context = Regex.Replace(context, @"[#*|✓✗\-]", "").Trim();

            if (context.Length is > 10 and < 200)
            {
                insights.Add(new LearningInsight
                {
                    Category = "skill_gap",
                    Content = context,
                    SourceTool = toolType,
                    SourceContext = userMessage.Length > 100 ? userMessage[..100] : userMessage,
                    JobApplicationId = jobApplicationId,
                });
            }

            break;
        }

        var actionPatterns = new[] { "nächster schritt:", "empfehlung:", "aktion:", "füge hinzu:", "ergänze:" };
        foreach (var pattern in actionPatterns)
        {
            var index = responseLower.IndexOf(pattern, StringComparison.Ordinal);
            if (index < 0)
                continue;

            var lineEnd = fullResponse.IndexOf('\n', index);
            if (lineEnd < 0)
                lineEnd = Math.Min(fullResponse.Length, index + 200);
            var action = fullResponse[index..lineEnd].Trim();

            if (action.Length is > 15 and < 200)
            {
                insights.Add(new LearningInsight
                {
                    Category = "action_item",
                    Content = action,
                    SourceTool = toolType,
                    JobApplicationId = jobApplicationId,
                });
            }

            break;
        }

        foreach (var insight in insights.Take(2))
            await learningMemoryService.AddInsight(userId, insight, cancellationToken).ConfigureAwait(false);
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

    private async Task PostProcessInterviewPrepAsync(string scopeUserId, string sessionId, string toolType, string finalReply)
    {
        if (toolType != "interviewprep")
            return;

        var askedQuestion = ExtractInterviewQuestion(finalReply);
        if (string.IsNullOrWhiteSpace(askedQuestion))
            return;

        await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx =>
        {
            if (!ctx.PractisedQuestions.Contains(askedQuestion, StringComparer.OrdinalIgnoreCase))
                ctx.PractisedQuestions.Add(askedQuestion);
        });
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

    /// <summary>
    /// Einmaliger KI-Aufruf ohne Streaming (z. B. CV-JSON-Extraktion). Groq zuerst, sonst Anthropic Haiku.
    /// <paramref name="maxTokens"/> wird auf maximal 800 begrenzt.
    /// </summary>
    public async Task<string> SingleCompletion(string prompt, int maxTokens = 600)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var capped = Math.Min(int.Clamp(maxTokens, 1, 800), 800);

        var groqMessages = new List<GroqChatMessage> { new() { Role = "user", Content = prompt } };
        var sampling = new GroqSamplingOptions(Temperature: 0.1, FrequencyPenalty: 0.1, PresencePenalty: 0.05);

        try
        {
            if (groqOptions.Value.UseAsPrimary && groqChat.IsConfigured)
            {
                var groqResult = await groqChat
                    .CompleteAsync(string.Empty, groqMessages, capped, sampling)
                    .ConfigureAwait(false);
                if (groqResult.Success && !string.IsNullOrWhiteSpace(groqResult.Content))
                    return groqResult.Content.Trim();

                logger.LogWarning(
                    "SingleCompletion: Groq failed or empty. Falling back to Anthropic. Error {Error}",
                    groqResult.Error);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SingleCompletion: Groq threw; falling back to Anthropic.");
        }

        var parameters = new MessageParameters
        {
            Model = AgentModelSelector.ResolveModel("general", config),
            MaxTokens = capped,
            Temperature = 0.1m,
            Messages = [new Message(RoleType.User, prompt)],
            System =
            [
                new SystemMessage(
                    "Antworte nur mit dem angeforderten Output (z. B. JSON), ohne Einleitung oder Schlussfloskeln."),
            ],
        };

        var response = await _client.Messages.GetClaudeMessageAsync(parameters).ConfigureAwait(false);
        return response.Message.ToString().Trim();
    }

    private static List<Tool> BuildTools(string toolType, AgentRequest request) => toolType switch
    {
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

    private async Task ApplyCareerToolSetupFromRequestAsync(
        string scopeUserId,
        string sessionId,
        string toolType,
        AgentRequest request)
    {
        var setup = request.CareerToolSetup;
        if (setup is null)
            return;

        if (toolType == "jobanalyzer")
        {
            var jobSource = !string.IsNullOrWhiteSpace(setup.JobUrl)
                ? setup.JobUrl.Trim()
                : setup.JobText?.Trim();
            if (!string.IsNullOrWhiteSpace(jobSource) && jobSource.Length >= 100)
            {
                try
                {
                    var jc = await jobExtractor.ExtractAsync(jobSource).ConfigureAwait(false);
                    await conversationService
                        .UpdateContextAsync(scopeUserId, sessionId, toolType, ctx => ctx.Job = jc)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "CareerToolSetup job extract failed. SessionId {SessionId}", sessionId);
                }
            }

            if (!string.IsNullOrWhiteSpace(setup.CvText))
            {
                var cv = setup.CvText.Trim();
                if (cv.Length > 3000)
                    cv = cv[..3000];
                await conversationService
                    .UpdateContextAsync(scopeUserId, sessionId, toolType, ctx => ctx.UserCV = cv)
                    .ConfigureAwait(false);
            }
        }
        else if (toolType == "interviewprep")
        {
            if (!string.IsNullOrWhiteSpace(setup.CvText))
            {
                var cv = setup.CvText.Trim();
                if (cv.Length > 3000)
                    cv = cv[..3000];
                await conversationService
                    .UpdateContextAsync(scopeUserId, sessionId, toolType, ctx => ctx.UserCV = cv)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(setup.JobTitle) || !string.IsNullOrWhiteSpace(setup.CompanyName))
            {
                await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx =>
                {
                    if (!string.IsNullOrWhiteSpace(setup.JobTitle))
                    {
                        var t = setup.JobTitle.Trim();
                        ctx.InterviewJobTitle = t.Length > 200 ? t[..200] : t;
                    }

                    if (!string.IsNullOrWhiteSpace(setup.CompanyName))
                    {
                        var c = setup.CompanyName.Trim();
                        ctx.InterviewCompany = c.Length > 200 ? c[..200] : c;
                    }
                }).ConfigureAwait(false);
            }

            var langCode = setup.InterviewLanguageCode?.Trim();
            if (!string.IsNullOrWhiteSpace(langCode))
            {
                var norm = langCode.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "de";
                await conversationService
                    .UpdateContextAsync(scopeUserId, sessionId, toolType, ctx => ctx.ConversationLanguage = norm)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task AppendConversationSummaryAsync(
        string scopeUserId,
        string sessionId,
        string toolType,
        string primaryUserQuestion,
        string assistantReply)
    {
        if (toolType is not ("jobanalyzer" or "interviewprep"))
            return;

        await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx =>
        {
            ctx.ConversationSummary = ConversationSummaryUpdater.MergeAfterTurn(
                ctx.ConversationSummary,
                primaryUserQuestion,
                assistantReply);
        }).ConfigureAwait(false);
    }

    private static bool ShouldRejectGroqReply(string reply, string toolType)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return true;
        if (toolType is not ("jobanalyzer" or "interviewprep"))
            return false;
        if (reply.Trim().Length < 72)
            return true;
        return HasHeavyDuplication(reply);
    }

    private static bool HasHeavyDuplication(string reply)
    {
        var sentences = Regex.Split(reply, @"(?<=[.!?])\s+")
            .Where(s => s.Trim().Length > 40)
            .Select(s => s.Trim().ToLowerInvariant())
            .ToList();
        if (sentences.Count < 4)
            return false;
        var groups = sentences.GroupBy(s => s).Where(g => g.Count() >= 3).ToList();
        return groups.Count > 0;
    }

    private async Task<string> TryRepairGroqReplyIfNeededAsync(string reply, string toolType)
    {
        if (toolType is not ("jobanalyzer" or "interviewprep") || !HasHeavyDuplication(reply))
            return reply;

        try
        {
            var body = reply.Length > 6000 ? reply[..6000] : reply;
            var prompt = $"""
                Überarbeite die folgende Assistenten-Antwort. Gleiche Sprache beibehalten.
                Regeln: Wiederholungen entfernen, knapper formulieren, inhaltlich gleiche Aussagen, Markdown-##-Struktur beibehalten wenn vorhanden.

                TEXT:
                {body}
                """;
            var repaired = await SingleCompletion(prompt, 700).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(repaired) || repaired.Trim().Length < 60)
                return reply;
            return repaired.Trim();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Groq repair completion skipped");
            return reply;
        }
    }

    private async Task UpdateConversationLanguageAsync(
        string scopeUserId,
        string sessionId,
        string toolType,
        string message,
        SessionContext context)
    {
        var detected = ConversationLanguageDetector.DetectLanguage(message);

        await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx =>
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

    /// <returns>True if context was updated (caller should refresh cached <see cref="SessionContext"/>).</returns>
    private async Task<bool> ExtractUserFactsAsync(string message, string scopeUserId, string sessionId, string toolType)
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
            return false;

        await conversationService.UpdateContextAsync(scopeUserId, sessionId, toolType, ctx =>
        {
            foreach (var fact in facts)
            {
                if (!ctx.UserFacts.Contains(fact, StringComparer.OrdinalIgnoreCase))
                    ctx.UserFacts.Add(fact);
            }

            if (ctx.UserFacts.Count > 30)
                ctx.UserFacts = ctx.UserFacts.TakeLast(30).ToList();
        });
        return true;
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
