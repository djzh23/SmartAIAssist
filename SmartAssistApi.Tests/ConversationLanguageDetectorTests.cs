using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class ConversationLanguageDetectorTests
{
    [Fact]
    public void DetectLanguage_GermanText_ReturnsDe()
    {
        const string input = "Ich habe eine Stelle gefunden und möchte meinen Lebenslauf verbessern.";
        var language = ConversationLanguageDetector.DetectLanguage(input);
        Assert.Equal("de", language);
    }

    [Fact]
    public void DetectLanguage_EnglishText_ReturnsEn()
    {
        const string input = "I found a job posting and want to improve my resume for this role.";
        var language = ConversationLanguageDetector.DetectLanguage(input);
        Assert.Equal("en", language);
    }

    [Fact]
    public void DetectLanguage_CodeSnippet_ReturnsNull()
    {
        const string input = "public class Demo { public string Name { get; set; } }";
        var language = ConversationLanguageDetector.DetectLanguage(input);
        Assert.Null(language);
    }

    [Fact]
    public void DetectLanguage_ShortAmbiguousText_ReturnsNull()
    {
        const string input = "ok";
        var language = ConversationLanguageDetector.DetectLanguage(input);
        Assert.Null(language);
    }
}
