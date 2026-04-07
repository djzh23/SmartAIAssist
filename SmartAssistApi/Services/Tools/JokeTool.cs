namespace SmartAssistApi.Services.Tools;

public static class JokeTool
{
    private static readonly string[] Jokes =
    [
        "Why do programmers prefer dark mode? Because light attracts bugs.",
        "I told my computer I needed a break. It said: no problem, I'll go to sleep.",
        "Why was the developer calm in production incidents? They had exceptional try-catch skills.",
        "Why don't skeletons fight each other? They don't have the guts.",
        "What do you call 8 hobbits? A hobbyte.",
    ];

    public static Task<string> GetJokeAsync()
    {
        var idx = Random.Shared.Next(Jokes.Length);
        return Task.FromResult(Jokes[idx]);
    }
}
