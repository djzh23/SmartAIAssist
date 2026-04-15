using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class ConversationServiceTests
{
    private const string U = "user_test_scope";

    [Fact]
    public async Task SaveHistoryAsync_IsolatesBySessionAndToolType()
    {
        var sut = new ConversationService();
        var s = "session-1";

        await sut.SaveHistoryAsync(U, s, "weather", [new Message(RoleType.User, "weather msg")]);
        await sut.SaveHistoryAsync(U, s, "jobanalyzer", [new Message(RoleType.User, "job msg")]);

        var weather = await sut.GetHistoryAsync(U, s, "weather");
        var jobs = await sut.GetHistoryAsync(U, s, "jobanalyzer");

        Assert.Single(weather);
        Assert.Single(jobs);
        Assert.Contains("weather msg", weather[0].ToString());
        Assert.Contains("job msg", jobs[0].ToString());
    }

    [Fact]
    public async Task DifferentScopeUsers_DoNotShareHistory_ForSameSessionId()
    {
        var sut = new ConversationService();
        var sessionId = "shared-session-id";

        await sut.SaveHistoryAsync("user_a", sessionId, "general", [new Message(RoleType.User, "secret A")]);
        var forB = await sut.GetHistoryAsync("user_b", sessionId, "general");

        Assert.Empty(forB);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsCopy_NotSharedReference()
    {
        var sut = new ConversationService();
        var sessionId = "session-copy";

        await sut.SaveHistoryAsync(U, sessionId, "general", [new Message(RoleType.User, "hello")]);
        var read1 = await sut.GetHistoryAsync(U, sessionId, "general");
        read1.Add(new Message(RoleType.Assistant, "mutated"));

        var read2 = await sut.GetHistoryAsync(U, sessionId, "general");
        Assert.Single(read2);
    }

    [Fact]
    public async Task SaveHistoryAsync_StoresSlidingWindow()
    {
        var sut = new ConversationService();
        var messages = Enumerable.Range(1, 25)
            .Select(i => new Message(RoleType.User, $"m{i}"))
            .ToList();

        await sut.SaveHistoryAsync(U, "session-window", "general", messages);
        var stored = await sut.GetHistoryAsync(U, "session-window", "general");

        Assert.Equal(ConversationService.MaxHistoryMessages, stored.Count);
        Assert.Contains("m20", stored.First().ToString());
        Assert.Contains("m25", stored.Last().ToString());
    }

    [Fact]
    public async Task Context_IsIsolatedBySessionAndToolType()
    {
        var sut = new ConversationService();

        await sut.UpdateContextAsync(U, "s1", "jobanalyzer", ctx => ctx.Job = new JobContext
        {
            IsAnalyzed = true,
            JobTitle = "Data Engineer",
            CompanyName = "A",
        });

        var other = await sut.GetContextAsync(U, "s1", "programming");
        var same = await sut.GetContextAsync(U, "s1", "jobanalyzer");

        Assert.Null(other.Job);
        Assert.NotNull(same.Job);
        Assert.Equal("Data Engineer", same.Job!.JobTitle);
    }

    [Fact]
    public async Task Context_IsolatedByScopeUser()
    {
        var sut = new ConversationService();

        await sut.UpdateContextAsync("user_a", "s1", "general", ctx => ctx.ConversationLanguage = "fr");
        var ctxB = await sut.GetContextAsync("user_b", "s1", "general");

        Assert.NotEqual("fr", ctxB.ConversationLanguage);
    }
}
