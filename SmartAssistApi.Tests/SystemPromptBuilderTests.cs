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

    [Fact]
    public void BuildPromptParts_General_LanguageModeOnlyInUncachedBlock()
    {
        var context = new SessionContext { ConversationLanguage = "de" };
        var request = new AgentRequest("Hi", SessionId: "s1", ToolType: "general");
        var parts = _sut.BuildPromptParts("general", context, request);

        Assert.Contains("PrivatePrep", parts.CachedPrefix);
        Assert.DoesNotContain("LANGUAGE MODE", parts.CachedPrefix);
        Assert.Contains("LANGUAGE MODE", parts.UncachedSystemBlock);
        Assert.Equal(_sut.BuildPrompt("general", context, request), parts.ToCombinedPrompt());
    }

    [Fact]
    public void BuildPromptParts_Programming_ProgrammingLanguageContextOnlyInUncached()
    {
        var context = new SessionContext
        {
            ConversationLanguage = "en",
            ProgrammingLanguage = "csharp",
            CurrentCodeContext = "var x = 1;"
        };
        var request = new AgentRequest("fix", SessionId: "s1", ToolType: "programming");

        var parts = _sut.BuildPromptParts("programming", context, request);

        Assert.DoesNotContain("PROGRAMMING CONTEXT", parts.CachedPrefix);
        Assert.Contains("PROGRAMMING CONTEXT: Language/Stack: csharp", parts.UncachedSystemBlock);
        Assert.Contains("var x = 1;", parts.DynamicToolSuffix);
        Assert.Contains("var x = 1;", parts.UncachedSystemBlock);
        Assert.Equal(_sut.BuildPrompt("programming", context, request), parts.ToCombinedPrompt());
    }

    [Fact]
    public void BuildPromptParts_JobAnalyzer_WithJob_ActiveContextOnlyInUncached()
    {
        var context = new SessionContext
        {
            ConversationLanguage = "de",
            Job = new JobContext
            {
                IsAnalyzed = true,
                JobTitle = "Dev",
                CompanyName = "Acme",
                Location = "Berlin",
                KeyRequirements = ["C#"],
                Keywords = ["dotnet"],
                RawJobText = "We need a developer."
            }
        };
        var request = new AgentRequest("Help", SessionId: "s1", ToolType: "jobanalyzer");
        var parts = _sut.BuildPromptParts("jobanalyzer", context, request);

        Assert.Contains("Stellenanalyse", parts.CachedPrefix);
        Assert.DoesNotContain("AKTIVE STELLE", parts.CachedPrefix);
        Assert.Contains("AKTIVE STELLE", parts.UncachedSystemBlock);
        Assert.Contains("Acme", parts.UncachedSystemBlock);
        Assert.Equal(_sut.BuildPrompt("jobanalyzer", context, request), parts.ToCombinedPrompt());
    }

    [Fact]
    public void BuildPromptParts_LanguageLearning_CacheableBlockIncludesLanguagePair()
    {
        var context = new SessionContext { ConversationLanguage = "de" };
        var request = new AgentRequest(
            "Hola",
            SessionId: "s1",
            ToolType: "language",
            LanguageLearningMode: true,
            NativeLanguage: "German",
            TargetLanguage: "Spanish");
        var parts = _sut.BuildPromptParts("language", context, request);

        Assert.Contains("German", parts.CachedPrefix);
        Assert.Contains("Spanish", parts.CachedPrefix);
        Assert.Contains("CONVERSATION LANGUAGE CONTEXT", parts.UncachedSystemBlock);
        Assert.Equal(_sut.BuildPrompt("language", context, request), parts.ToCombinedPrompt());
    }
}

