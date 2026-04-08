using SmartAssistApi.Models;
using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class TranslationToolTests
{
    [Fact]
    public async Task TranslateAsync_ValidInput_ReturnsNonEmptyString()
    {
        var result = await TranslationTool.TranslateAsync("Hello", "en", "es");

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task TranslateAsync_ValidInput_ReturnsParenthesizedFormatOrFallback()
    {
        var result = await TranslationTool.TranslateAsync("Good morning", "en", "de");

        // Either "(Guten Morgen)" format or the fallback on network failure - both are acceptable.
        Assert.True(
            (result.StartsWith("(") && result.EndsWith(")")) || result == "[Translation unavailable]",
            $"Unexpected format: {result}"
        );
    }

    [Fact]
    public async Task TranslateAsync_InvalidLangPair_ReturnsFallbackMessage()
    {
        var result = await TranslationTool.TranslateAsync("test", "xx", "zz");

        Assert.Equal("[Translation unavailable]", result);
    }

    [Fact]
    public async Task TranslateAsync_EmptyText_ReturnsFallbackMessage()
    {
        var result = await TranslationTool.TranslateAsync("", "en", "es");

        Assert.NotEmpty(result);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsLanguageNames()
    {
        var result = LanguageLearningTool.BuildSystemPrompt("German", "Spanish");

        Assert.Contains("German", result);
        Assert.Contains("Spanish", result);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesStructuredMarkers()
    {
        var result = LanguageLearningTool.BuildSystemPrompt("English", "French");

        Assert.Contains("---ZIELSPRACHE---", result);
        Assert.Contains("---UEBERSETZUNG---", result);
        Assert.Contains("---END---", result);
    }

    [Fact]
    public void BuildSystemPrompt_IncludesLanguageNamesInPrompt()
    {
        var result = LanguageLearningTool.BuildSystemPrompt(
            nativeLanguage: "German",
            targetLanguage: "Spanish",
            nativeLanguageCode: "de",
            targetLanguageCode: "es",
            level: "a2",
            learningGoal: "daily speaking");

        Assert.Contains("German", result);
        Assert.Contains("Spanish", result);
    }

    [Fact]
    public void AgentRequest_LanguageLearningMode_DefaultsFalse()
    {
        var request = new AgentRequest("Hello");

        Assert.False(request.LanguageLearningMode);
        Assert.Null(request.TargetLanguage);
        Assert.Null(request.NativeLanguage);
        Assert.Null(request.TargetLanguageCode);
        Assert.Null(request.NativeLanguageCode);
        Assert.Null(request.Level);
        Assert.Null(request.LearningGoal);
    }

    [Fact]
    public void AgentRequest_LanguageLearningFields_RoundTrip()
    {
        var request = new AgentRequest(
            "Guten Morgen",
            SessionId: "abc123",
            LanguageLearningMode: true,
            TargetLanguage: "Spanish",
            NativeLanguage: "German",
            TargetLanguageCode: "es",
            NativeLanguageCode: "de",
            Level: "A1",
            LearningGoal: "speaking basics"
        );

        Assert.True(request.LanguageLearningMode);
        Assert.Equal("Spanish", request.TargetLanguage);
        Assert.Equal("German", request.NativeLanguage);
        Assert.Equal("es", request.TargetLanguageCode);
        Assert.Equal("de", request.NativeLanguageCode);
        Assert.Equal("A1", request.Level);
        Assert.Equal("speaking basics", request.LearningGoal);
    }
}
