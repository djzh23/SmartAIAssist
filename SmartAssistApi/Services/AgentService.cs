using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;
using SmartAssistApi.Services.Tools;
using System.Reflection;
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
                ([FunctionParameter("Name der Stadt", true)] string city) => GetWeather(city)
            ),
            Tool.FromFunc(
                "summarize_text",
                ([FunctionParameter("Der zu kürzende Text", true)] string text) => Summarize(text)
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

            foreach (var toolCall in response.ToolCalls)
            {
                var result = toolCall.Invoke<string>();
                messages.Add(new Message(toolCall, result));
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

    private static string GetWeather(string city)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["berlin"] = "18°C, bewölkt",
            ["hamburg"] = "14°C, regnerisch",
            ["münchen"] = "22°C, sonnig"
        };
        return data.TryGetValue(city, out var w)
            ? $"Wetter in {city}: {w}"
            : $"Keine Daten für {city}.";
    }

    private static string Summarize(string text)
    {
        var words = text.Split(' ');
        return $"Zusammenfassung ({words.Length} Wörter): {string.Join(" ", words.Take(15))}...";
    }
}
