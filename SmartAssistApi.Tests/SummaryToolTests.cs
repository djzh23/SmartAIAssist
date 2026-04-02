using SmartAssistApi.Services.Tools;

namespace SmartAssistApi.Tests;

public class SummaryToolTests
{
    [Fact]
    public void Summarize_TextWithMoreThan15Words_ReturnsFirst15WordsWithEllipsis()
    {
        var words = Enumerable.Range(1, 20).Select(i => $"word{i}");
        var text = string.Join(" ", words);

        var result = SummaryTool.Summarize(text);

        Assert.Contains("word15", result);
        Assert.DoesNotContain("word16", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Summarize_TextWithExactly15Words_ReturnsAllWordsWithEllipsis()
    {
        var text = string.Join(" ", Enumerable.Range(1, 15).Select(i => $"word{i}"));

        var result = SummaryTool.Summarize(text);

        Assert.Contains("word15", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Summarize_TextWithFewerThan15Words_ReturnsAllWords()
    {
        var text = "one two three four five";

        var result = SummaryTool.Summarize(text);

        Assert.Contains("one", result);
        Assert.Contains("five", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Summarize_SingleWord_ReturnsThatWordWithEllipsis()
    {
        var result = SummaryTool.Summarize("hello");

        Assert.Contains("hello", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Summarize_ReportsCorrectWordCount()
    {
        var text = "one two three four five";

        var result = SummaryTool.Summarize(text);

        Assert.Contains("5", result);
    }
}
