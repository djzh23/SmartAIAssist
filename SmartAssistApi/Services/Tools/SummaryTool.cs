namespace SmartAssistApi.Services.Tools;

public static class SummaryTool
{
    public static string Summarize(string text)
    {
        var words = text.Split(' ').Length;
        return $"[Zusammenfassung von {words} Wörtern]: {string.Join(" ", text.Split(' ').Take(15))}...";
    }
}