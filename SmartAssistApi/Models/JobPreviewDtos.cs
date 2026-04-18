namespace SmartAssistApi.Models;

public sealed record JobPreviewRequest(string Input);

public sealed record JobPreviewResponse(
    bool Success,
    string? JobTitle,
    string? CompanyName,
    string? Location,
    string? RawJobText,
    IReadOnlyList<string>? KeyRequirements,
    IReadOnlyList<string>? Keywords,
    string? Error);
