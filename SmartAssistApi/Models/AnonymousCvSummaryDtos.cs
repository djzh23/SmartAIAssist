namespace SmartAssistApi.Models;

public sealed class AnonymousCvSummaryRequest
{
    /// <summary><c>de</c> or <c>en</c>; default German.</summary>
    public string? Language { get; set; }
}

public sealed record AnonymousCvSummaryResponse(string Summary);
