using System.Runtime.CompilerServices;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;
using SmartAssistApi.Services.Tools;
using Tool = Anthropic.SDK.Common.Tool;

namespace SmartAssistApi.Services;

public class AgentService(IConfiguration config) : IAgentService
{
    private readonly AnthropicClient _client = new(config["ANTHROPIC_API_KEY"]!);

    public async Task<AgentResponse> RunAsync(AgentRequest request)
    {
        var isLanguageLearning = request.LanguageLearningMode
            && request.NativeLanguage is not null
            && request.TargetLanguage is not null;

        var tools = new List<Tool>
        {
            Tool.FromFunc(
                "get_weather",
                ([FunctionParameter("Name der Stadt", true)] string city) =>
                    WeatherTool.GetWeatherAsync(city).GetAwaiter().GetResult()
            ),
            Tool.FromFunc(
                "analyze_job",
                ([FunctionParameter("Job posting text or URL of the job listing", true)] string input) =>
                    JobAnalyzerTool.AnalyzeJobAsync(input).GetAwaiter().GetResult()
            ),
            Tool.FromFunc(
                "translate_text",
                ([FunctionParameter("Text to translate", true)] string text,
                 [FunctionParameter("Target language code (e.g. es, fr, de)", true)] string targetLanguage,
                 [FunctionParameter("Native language code (e.g. de, en)", true)] string nativeLanguage) =>
                    TranslationTool.TranslateAsync(text, nativeLanguage, targetLanguage).GetAwaiter().GetResult()
            )
        };

        var messages = new List<Message>
        {
            new(RoleType.User, request.Message)
        };

        var parameters = new MessageParameters
        {
            Model = config["Anthropic:Model"]!,
            MaxTokens = config.GetValue<int>("Anthropic:MaxTokens"),
            Stream = false,
            Temperature = config.GetValue<decimal>("Anthropic:Temperature"),
            Messages = messages,
            Tools = tools
        };

        if (isLanguageLearning)
        {
            parameters.System = new List<SystemMessage>
            {
                new(LanguageLearningTool.BuildSystemPrompt(
                    request.NativeLanguage!,
                    request.TargetLanguage!,
                    request.NativeLanguageCode,
                    request.TargetLanguageCode,
                    request.Level,
                    request.LearningGoal))
            };
        }

        var response = await _client.Messages.GetClaudeMessageAsync(parameters);

        if (response.ToolCalls != null && response.ToolCalls.Any())
        {
            messages.Add(response.Message);

            string? jobText = null;
            foreach (var toolCall in response.ToolCalls)
            {
                var result = toolCall.Invoke<string>();
                if (result.StartsWith("JOB_ANALYSIS_REQUEST:", StringComparison.Ordinal))
                    jobText = result["JOB_ANALYSIS_REQUEST:".Length..];
                messages.Add(new Message(toolCall, result));
            }

            // Job analysis: make a dedicated Claude call with job analysis system prompt
            if (jobText is not null)
            {
                var jobParams = new MessageParameters
                {
                    Model = config["Anthropic:Model"]!,
                    MaxTokens = config.GetValue<int>("Anthropic:MaxTokens"),
                    Stream = false,
                    Temperature = config.GetValue<decimal>("Anthropic:Temperature"),
                    Messages = new List<Message>
                    {
                        new(RoleType.User, $"Job posting:\n\n{jobText}")
                    },
                    System = new List<SystemMessage>
                    {
                        new(JobAnalyzerTool.BuildJobAnalysisPrompt())
                    }
                };
                var jobResponse = await _client.Messages.GetClaudeMessageAsync(jobParams);
                return new AgentResponse(jobResponse.Message.ToString(), "analyze_job");
            }

            var final = await _client.Messages.GetClaudeMessageAsync(parameters);
            var finalText = final.Message.ToString();

            if (isLanguageLearning)
            {
                var learningData = LanguageLearningTool.ParseResponse(finalText);
                if (learningData is not null)
                    return new AgentResponse(learningData.TargetLanguageText, response.ToolCalls.First().Name, learningData);
            }

            return new AgentResponse(finalText, response.ToolCalls.First().Name);
        }

        var rawText = response.Message.ToString();

        if (isLanguageLearning)
        {
            var learningData = LanguageLearningTool.ParseResponse(rawText);
            if (learningData is not null)
                return new AgentResponse(learningData.TargetLanguageText, null, learningData);
        }

        return new AgentResponse(rawText);
    }

    public async IAsyncEnumerable<AgentStreamChunk> StreamAsync(
        AgentRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var isLanguageLearning = request.LanguageLearningMode
            && request.NativeLanguage is not null
            && request.TargetLanguage is not null;

        var tools = BuildTools();
        var messages = new List<Message> { new(RoleType.User, request.Message) };
        var baseParams = BuildParameters(messages, tools, isLanguageLearning, request);

        // ── First call: non-streaming to detect tool use ──────────────────────
        // Tool-calling requires a round-trip regardless, so the overhead is minimal.
        var firstResponse = await _client.Messages.GetClaudeMessageAsync(baseParams);

        if (firstResponse.ToolCalls is { Count: > 0 })
        {
            messages.Add(firstResponse.Message);

            string? jobText = null;
            foreach (var toolCall in firstResponse.ToolCalls)
            {
                var result = toolCall.Invoke<string>();
                if (result.StartsWith("JOB_ANALYSIS_REQUEST:", StringComparison.Ordinal))
                    jobText = result["JOB_ANALYSIS_REQUEST:".Length..];
                messages.Add(new Message(toolCall, result));
            }

            if (jobText is not null)
            {
                // Stream the dedicated job-analysis response
                var jobParams = new MessageParameters
                {
                    Model     = config["Anthropic:Model"]!,
                    MaxTokens = config.GetValue<int>("Anthropic:MaxTokens"),
                    Stream    = true,
                    Temperature = config.GetValue<decimal>("Anthropic:Temperature"),
                    Messages  = new List<Message> { new(RoleType.User, $"Job posting:\n\n{jobText}") },
                    System    = new List<SystemMessage> { new(JobAnalyzerTool.BuildJobAnalysisPrompt()) },
                };
                await foreach (var chunk in StreamTextAsync(jobParams, ct))
                    yield return AgentStreamChunk.TextPart(chunk);
                yield return AgentStreamChunk.Done("analyze_job");
                yield break;
            }

            // Stream the follow-up response after tool results
            baseParams.Messages = messages;
            baseParams.Stream   = true;
            await foreach (var chunk in StreamTextAsync(baseParams, ct))
                yield return AgentStreamChunk.TextPart(chunk);
            yield return AgentStreamChunk.Done(firstResponse.ToolCalls.First().Name);
            yield break;
        }

        // ── No tools — re-stream the first response's content ─────────────────
        // We already have the full text; emit it in one chunk for simplicity,
        // OR re-issue as a streaming call for incremental display.
        var streamParams = BuildParameters(
            new List<Message> { new(RoleType.User, request.Message) }, tools, isLanguageLearning, request);
        streamParams.Stream = true;
        await foreach (var chunk in StreamTextAsync(streamParams, ct))
            yield return AgentStreamChunk.TextPart(chunk);
        yield return AgentStreamChunk.Done();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private List<Tool> BuildTools() =>
    [
        Tool.FromFunc("get_weather",
            ([FunctionParameter("Name der Stadt", true)] string city) =>
                WeatherTool.GetWeatherAsync(city).GetAwaiter().GetResult()),
        Tool.FromFunc("analyze_job",
            ([FunctionParameter("Job posting text or URL", true)] string input) =>
                JobAnalyzerTool.AnalyzeJobAsync(input).GetAwaiter().GetResult()),
        Tool.FromFunc("translate_text",
            ([FunctionParameter("Text to translate", true)] string text,
             [FunctionParameter("Target language code", true)] string targetLanguage,
             [FunctionParameter("Native language code",  true)] string nativeLanguage) =>
                TranslationTool.TranslateAsync(text, nativeLanguage, targetLanguage).GetAwaiter().GetResult()),
    ];

    private MessageParameters BuildParameters(
        List<Message> messages, List<Tool> tools, bool isLanguageLearning, AgentRequest request)
    {
        var p = new MessageParameters
        {
            Model       = config["Anthropic:Model"]!,
            MaxTokens   = config.GetValue<int>("Anthropic:MaxTokens"),
            Stream      = false,
            Temperature = config.GetValue<decimal>("Anthropic:Temperature"),
            Messages    = messages,
            Tools       = tools,
        };
        if (isLanguageLearning)
            p.System = new List<SystemMessage>
            {
                new(LanguageLearningTool.BuildSystemPrompt(
                    request.NativeLanguage!, request.TargetLanguage!,
                    request.NativeLanguageCode, request.TargetLanguageCode,
                    request.Level, request.LearningGoal))
            };
        return p;
    }

    private async IAsyncEnumerable<string> StreamTextAsync(
        MessageParameters parameters,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var streamEvent in _client.Messages.StreamClaudeMessageAsync(parameters, ct))
        {
            if (streamEvent.Delta?.Type == "text_delta"
                && streamEvent.Delta.Text is { Length: > 0 } text)
                yield return text;
        }
    }
}
