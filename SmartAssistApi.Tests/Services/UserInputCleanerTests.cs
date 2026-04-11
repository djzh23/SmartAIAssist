using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class UserInputCleanerTests
{
    [Fact]
    public void CleanUserInput_RemovesHtmlTags()
    {
        var input = "<div class=\"job\"><h2>Developer</h2><p>React needed</p></div>";
        var result = UserInputCleaner.CleanUserInput(input);
        Assert.DoesNotContain("<", result);
        Assert.Contains("Developer", result);
        Assert.Contains("React", result);
    }

    [Fact]
    public void CleanUserInput_NormalizesWhitespaceAndTabs()
    {
        var input = "Zeile 1\n\n\n\n\nZeile 2     mit\t  Leerzeichen";
        var result = UserInputCleaner.CleanUserInput(input);
        Assert.DoesNotContain("\n\n\n", result);
        Assert.DoesNotContain("     ", result);
    }

    [Fact]
    public void CleanUserInput_TruncatesAt4000Chars()
    {
        var input = new string('A', 5000);
        var result = UserInputCleaner.CleanUserInput(input);
        Assert.True(result.Length <= 4025);
        Assert.Contains("gekürzt", result);
    }

    [Fact]
    public void CleanUserInput_PreservesNormalText()
    {
        var input = "Analysiere diese Stelle: React Developer bei SAP";
        var result = UserInputCleaner.CleanUserInput(input);
        Assert.Equal(input, result);
    }
}
