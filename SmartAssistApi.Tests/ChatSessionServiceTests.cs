using Microsoft.Extensions.Logging.Abstractions;
using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class ChatSessionServiceTests
{
    private readonly MemoryRedisStringStore _redis = new();
    private readonly ConversationService _conv = new();
    private readonly ChatSessionService _service;

    public ChatSessionServiceTests()
    {
        _service = new ChatSessionService(_redis, _conv, NullLogger<ChatSessionService>.Instance);
    }

    [Fact]
    public async Task GetSessions_NewUser_ReturnsEmptyList()
    {
        var sessions = await _service.GetSessions("new_user");
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task CreateSession_ReturnsSessionWithId()
    {
        var session = await _service.CreateSession("user1", "jobanalyzer", null);
        Assert.False(string.IsNullOrWhiteSpace(session.Id));
        Assert.Equal("Stellenanalyse", session.Title);
    }

    [Fact]
    public async Task Sessions_DifferentUsers_AreIsolated()
    {
        await _service.CreateSession("user_A", "general", null);
        await _service.CreateSession("user_B", "code", null);

        var sessionsA = await _service.GetSessions("user_A");
        var sessionsB = await _service.GetSessions("user_B");

        Assert.Single(sessionsA);
        Assert.Single(sessionsB);
        Assert.NotEqual(sessionsA[0].ToolType, sessionsB[0].ToolType);
    }

    [Fact]
    public async Task MaxSessions_OldestDeleted()
    {
        for (var i = 0; i < 55; i++)
            await _service.CreateSession("user1", "general", null);

        var sessions = await _service.GetSessions("user1");
        Assert.Equal(ChatSessionService.MaxSessions, sessions.Count);
    }
}
