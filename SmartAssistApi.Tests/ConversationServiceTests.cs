using Anthropic.SDK.Messaging;
using SmartAssistApi.Models;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class ConversationServiceTests
{
    [Fact]
    public async Task SaveHistoryAsync_IsolatesBySessionAndToolType()
    {
        var sut = new ConversationService();
        var s = "session-1";

        await sut.SaveHistoryAsync(s, "weather", [new Message(RoleType.User, "weather msg")]);
        await sut.SaveHistoryAsync(s, "jobanalyzer", [new Message(RoleType.User, "job msg")]);

        var weather = await sut.GetHistoryAsync(s, "weather");
        var jobs = await sut.GetHistoryAsync(s, "jobanalyzer");

        Assert.Single(weather);
        Assert.Single(jobs);
        Assert.Contains("weather msg", weather[0].ToString());
        Assert.Contains("job msg", jobs[0].ToString());
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsCopy_NotSharedReference()
    {
        var sut = new ConversationService();
        var sessionId = "session-copy";

        await sut.SaveHistoryAsync(sessionId, "general", [new Message(RoleType.User, "hello")]);
        var read1 = await sut.GetHistoryAsync(sessionId, "general");
        read1.Add(new Message(RoleType.Assistant, "mutated"));

        var read2 = await sut.GetHistoryAsync(sessionId, "general");
        Assert.Single(read2);
    }

    [Fact]
    public async Task SaveHistoryAsync_StoresSlidingWindow()
    {
        var sut = new ConversationService();
        var messages = Enumerable.Range(1, 25)
            .Select(i => new Message(RoleType.User, $"m{i}"))
            .ToList();

        await sut.SaveHistoryAsync("session-window", "general", messages);
        var stored = await sut.GetHistoryAsync("session-window", "general");

        Assert.Equal(ConversationService.MaxHistoryMessages, stored.Count);
        Assert.Contains("m20", stored.First().ToString());
        Assert.Contains("m25", stored.Last().ToString());
    }

    [Fact]
    public async Task Context_IsIsolatedBySessionAndToolType()
    {
        var sut = new ConversationService();

        await sut.UpdateContextAsync("s1", "jobanalyzer", ctx => ctx.Job = new JobContext
        {
            IsAnalyzed = true,
            JobTitle = "Data Engineer",
            CompanyName = "A"
        });

        var other = await sut.GetContextAsync("s1", "programming");
        var same = await sut.GetContextAsync("s1", "jobanalyzer");

        Assert.Null(other.Job);
        Assert.NotNull(same.Job);
        Assert.Equal("Data Engineer", same.Job!.JobTitle);
    }
}
