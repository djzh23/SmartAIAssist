using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests.Services;

public class LlmHistoryTrimmerTests
{
    private static SessionContext EmptyContext() => new() { SessionId = "s", ToolType = "general" };

    [Fact]
    public void Trim_UnderLimit_ReturnsAll()
    {
        var history = new List<Message>
        {
            new(RoleType.User, "a"),
            new(RoleType.Assistant, "b"),
        };
        var result = LlmHistoryTrimmer.Trim(history, "general", EmptyContext());
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Trim_General_TrimsToLastSix_WhenManyMessages()
    {
        var history = Enumerable.Range(0, 12)
            .Select(i => i % 2 == 0
                ? new Message(RoleType.User, $"u{i}")
                : new Message(RoleType.Assistant, $"a{i}"))
            .ToList();

        var result = LlmHistoryTrimmer.Trim(history, "general", EmptyContext());
        Assert.Equal(6, result.Count);
        Assert.Contains("u10", result[4].ToString());
    }

    [Fact]
    public void Trim_LongAssistantInHistory_IsTruncatedExceptLastTwoMessages()
    {
        var longAssistant = new string('X', 500);
        var history = new List<Message>
        {
            new(RoleType.User, "Frage 1"),
            new(RoleType.Assistant, longAssistant),
            new(RoleType.User, "Frage 2"),
            new(RoleType.Assistant, "Kurze Antwort"),
            new(RoleType.User, "Frage 3"),
            new(RoleType.Assistant, "Aktuelle Antwort"),
        };

        var result = LlmHistoryTrimmer.Trim(history, "general", EmptyContext());
        Assert.Equal(6, result.Count);
        var firstAssistantText = result[1].ToString();
        Assert.True(firstAssistantText.Length <= 320, $"Expected truncated assistant, got length {firstAssistantText.Length}");
        Assert.Contains("…", firstAssistantText);
        Assert.Equal("Aktuelle Antwort", result[5].ToString());
    }
}
