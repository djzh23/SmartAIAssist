using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class SystemPromptBuilderTests
{
    private readonly SystemPromptBuilder _sut = new();

    [Fact]
    public void BuildPrompt_GermanConversation_ContainsGermanLanguageRule()
    {
        var context = new SessionContext { ConversationLanguage = "de" };
        var request = new AgentRequest("Hallo", SessionId: "s1", ToolType: "general");

        var prompt = _sut.BuildPrompt("general", context, request);

        Assert.Contains("Always answer in German", prompt);
    }

    [Fact]
    public void BuildPrompt_EnglishConversation_ContainsEnglishLanguageRule()
    {
        var context = new SessionContext { ConversationLanguage = "en" };
        var request = new AgentRequest("Hello", SessionId: "s1", ToolType: "general");

        var prompt = _sut.BuildPrompt("general", context, request);

        Assert.Contains("Always answer in English", prompt);
    }

    [Fact]
    public void BuildPrompt_LanguageTool_UsesConversationContextInstruction()
    {
        var context = new SessionContext { ConversationLanguage = "en" };
        var request = new AgentRequest("Translate this", SessionId: "s1", ToolType: "language");

        var prompt = _sut.BuildPrompt("language", context, request);

        Assert.Contains("CONVERSATION LANGUAGE CONTEXT", prompt);
        Assert.Contains("prefer English", prompt);
    }
}
