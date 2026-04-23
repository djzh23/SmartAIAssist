namespace CvStudio.Application.Services;

public sealed class AtsImprovement
{
    public string Category { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public string Priority { get; set; } = "Mittel";

    public string PriorityColor => Priority switch
    {
        "Hoch" => "#C9392B",
        "Mittel" => "#D97706",
        _ => "#16A34A"
    };

    public string PriorityIcon => Priority switch
    {
        "Hoch" => "🔴",
        "Mittel" => "🟡",
        _ => "🟢"
    };
}