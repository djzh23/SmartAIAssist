using System.ComponentModel.DataAnnotations;

namespace SmartAssistApi.Models;

public class SetContextRequest
{
    [StringLength(80)]
    public string? SessionId { get; set; }

    [StringLength(40)]
    public string? ToolType { get; set; }

    [StringLength(16_000)]
    public string? CVText { get; set; }

    [StringLength(400)]
    public string? JobTitle { get; set; }

    [StringLength(400)]
    public string? CompanyName { get; set; }

    [StringLength(80)]
    public string? ProgrammingLanguage { get; set; }
}
