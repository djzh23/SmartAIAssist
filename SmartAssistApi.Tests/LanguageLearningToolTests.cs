using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class LanguageLearningToolTests
{
    [Fact]
    public void ParseResponse_ValidJson_ReturnsParsedResponse()
    {
        var json = """{"target": "¡Hola! ¿Cómo estás?", "native": "Hallo! Wie geht es dir?", "tip": "estás = informal 'you are'"}""";

        var result = LanguageLearningTool.ParseResponse(json);

        Assert.NotNull(result);
        Assert.Equal("¡Hola! ¿Cómo estás?", result.TargetLanguageText);
        Assert.Equal("Hallo! Wie geht es dir?", result.NativeLanguageText);
        Assert.Equal("estás = informal 'you are'", result.LearnTip);
    }

    [Fact]
    public void ParseResponse_NullTip_ReturnsNullLearnTip()
    {
        var json = """{"target": "Buenos días.", "native": "Guten Morgen.", "tip": null}""";

        var result = LanguageLearningTool.ParseResponse(json);

        Assert.NotNull(result);
        Assert.Null(result.LearnTip);
    }

    [Fact]
    public void ParseResponse_JsonWrappedInMarkdown_ExtractsCorrectly()
    {
        var json = "```json\n{\"target\": \"Hola.\", \"native\": \"Hallo.\", \"tip\": null}\n```";

        var result = LanguageLearningTool.ParseResponse(json);

        Assert.NotNull(result);
        Assert.Equal("Hola.", result.TargetLanguageText);
    }

    [Fact]
    public void ParseResponse_InvalidJson_ReturnsNull()
    {
        var result = LanguageLearningTool.ParseResponse("This is not JSON at all.");

        Assert.Null(result);
    }

    [Fact]
    public void ParseResponse_EmptyString_ReturnsNull()
    {
        var result = LanguageLearningTool.ParseResponse("");

        Assert.Null(result);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsLanguageNames()
    {
        var prompt = LanguageLearningTool.BuildSystemPrompt("German", "Spanish");

        Assert.Contains("German", prompt);
        Assert.Contains("Spanish", prompt);
        Assert.Contains("---ZIELSPRACHE---", prompt);
        Assert.Contains("---UEBERSETZUNG---", prompt);
        Assert.Contains("---END---", prompt);
    }
}
