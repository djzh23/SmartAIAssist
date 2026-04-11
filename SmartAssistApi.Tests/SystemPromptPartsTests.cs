using SmartAssistApi.Models;

namespace SmartAssistApi.Tests;

public class SystemPromptPartsTests
{
    [Fact]
    public void ToCombinedPrompt_EmptyCachedPrefix_ThrowsWithClearMessage()
    {
        var parts = new SystemPromptParts("   ", string.Empty, "LANGUAGE MODE: x");

        var ex = Assert.Throws<InvalidOperationException>(() => parts.ToCombinedPrompt());
        Assert.Contains("cached prefix is empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToCombinedPrompt_EmptyLanguageRule_ThrowsWithClearMessage()
    {
        var parts = new SystemPromptParts("You are helpful.", string.Empty, "  ");

        var ex = Assert.Throws<InvalidOperationException>(() => parts.ToCombinedPrompt());
        Assert.Contains("language rule is empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToCombinedPrompt_NoToolSuffix_JoinsLanguageWithBlankLine()
    {
        var parts = new SystemPromptParts("PREFIX", string.Empty, "SUFFIX");
        Assert.Equal("PREFIX\n\nSUFFIX", parts.ToCombinedPrompt());
    }

    [Fact]
    public void ToCombinedPrompt_WithToolSuffix_ConcatenatesPrefixAndTailThenLanguage()
    {
        var parts = new SystemPromptParts("A", "B", "C");
        // Tool prompt is CachedPrefix + DynamicToolSuffix (no extra joiner — matches real prompts).
        Assert.Equal("AB\n\nC", parts.ToCombinedPrompt());
    }

    [Fact]
    public void UncachedSystemBlock_WithDynamicTail_AppendsLanguage()
    {
        var parts = new SystemPromptParts("cached", "dynamic", "lang");
        Assert.Equal("dynamic\n\nlang", parts.UncachedSystemBlock);
    }

    [Fact]
    public void UncachedSystemBlock_WithoutDynamicTail_IsLanguageOnly()
    {
        var parts = new SystemPromptParts("cached", string.Empty, "lang");
        Assert.Equal("lang", parts.UncachedSystemBlock);
    }

    [Fact]
    public void WithProfilePrefix_PrependsToUncachedBlock()
    {
        var parts = new SystemPromptParts("cached", "dynamic", "lang");
        var next = parts.WithProfilePrefix("[NUTZERPROFIL]\nZeile");
        Assert.Equal("[NUTZERPROFIL]\nZeile\n\ndynamic\n\nlang", next.UncachedSystemBlock);
    }

    [Fact]
    public void WithProfilePrefix_EmptyOrWhitespace_ReturnsSame()
    {
        var parts = new SystemPromptParts("a", "b", "c");
        Assert.Same(parts, parts.WithProfilePrefix(""));
        Assert.Same(parts, parts.WithProfilePrefix("   "));
    }
}
