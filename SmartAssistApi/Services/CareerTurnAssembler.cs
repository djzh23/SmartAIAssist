using System.Text;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Baut die effektive Nutzerrolle aus kurzer Frage + strukturiertem Setup (Backend autoritativ).</summary>
public static class CareerTurnAssembler
{
    public static bool HasStructuredSetup(AgentRequest request) =>
        request.CareerToolSetup is { } s
        && (s.GeneralCoaching
            || !string.IsNullOrWhiteSpace(s.CvText)
            || !string.IsNullOrWhiteSpace(s.JobText)
            || !string.IsNullOrWhiteSpace(s.JobUrl)
            || !string.IsNullOrWhiteSpace(s.JobTitle)
            || !string.IsNullOrWhiteSpace(s.CompanyName));

    public static bool ShouldComposeStructuredTurn(AgentRequest request, string toolType) =>
        (toolType == "jobanalyzer" || toolType == "interviewprep")
        && request.CareerToolSetup is not null
        && HasStructuredSetup(request);

    public static string ComposeEffectiveUserMessage(AgentRequest request, string toolType, string cleanedPrimaryMessage)
    {
        if (!ShouldComposeStructuredTurn(request, toolType))
            return cleanedPrimaryMessage;

        var setup = request.CareerToolSetup!;
        var sb = new StringBuilder();

        if (setup.GeneralCoaching)
        {
            sb.AppendLine("[META]");
            sb.AppendLine("Modus: Allgemeines Coaching ohne feste Stellenanzeige. Nutze CV/Profil-Snippets, stelle klärende Fragen wenn nötig, keine erfundene Stelle.");
            sb.AppendLine("[ENDE_META]");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(setup.CvText))
        {
            sb.AppendLine("[SETUP_CV]");
            sb.AppendLine(setup.CvText.Trim());
            sb.AppendLine("[ENDE_SETUP_CV]");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(setup.JobUrl))
        {
            sb.AppendLine("[SETUP_JOB_URL]");
            sb.AppendLine(setup.JobUrl.Trim());
            sb.AppendLine("[ENDE_SETUP_JOB_URL]");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(setup.JobText))
        {
            sb.AppendLine("[SETUP_JOB]");
            sb.AppendLine(setup.JobText.Trim());
            sb.AppendLine("[ENDE_SETUP_JOB]");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(setup.JobTitle) || !string.IsNullOrWhiteSpace(setup.CompanyName))
        {
            sb.AppendLine("[SETUP_ROLE]");
            if (!string.IsNullOrWhiteSpace(setup.JobTitle))
                sb.AppendLine($"Titel: {setup.JobTitle.Trim()}");
            if (!string.IsNullOrWhiteSpace(setup.CompanyName))
                sb.AppendLine($"Firma: {setup.CompanyName.Trim()}");
            sb.AppendLine("[ENDE_SETUP_ROLE]");
            sb.AppendLine();
        }

        if (toolType == "jobanalyzer" && setup.JobAnalyzerFollowUp)
        {
            sb.AppendLine("[META]");
            sb.AppendLine("Session: Folgefrage (keine vollständige Erstanalyse von vorn, außer User verlangt es).");
            sb.AppendLine("[ENDE_META]");
            sb.AppendLine();
        }

        if (toolType == "interviewprep" && !string.IsNullOrWhiteSpace(setup.InterviewAlias))
        {
            sb.AppendLine("[META]");
            sb.AppendLine($"Kandidatenname: {setup.InterviewAlias.Trim()}");
            sb.AppendLine("[ENDE_META]");
            sb.AppendLine();
        }

        sb.AppendLine("[AKTUELLE_NUTZERFRAGE]");
        sb.AppendLine(string.IsNullOrWhiteSpace(cleanedPrimaryMessage) ? "." : cleanedPrimaryMessage.Trim());
        sb.AppendLine("[ENDE_AKTUELLE_NUTZERFRAGE]");

        var composed = sb.ToString();
        return composed.Length > AgentPayloadLimits.MaxTotalPayloadChars
            ? composed[..AgentPayloadLimits.MaxTotalPayloadChars]
            : composed;
    }
}
