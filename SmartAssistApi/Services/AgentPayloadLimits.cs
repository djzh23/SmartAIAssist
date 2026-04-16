using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Deckelt strukturierte Career-Payloads für TPM und Missbrauchsschutz.</summary>
public static class AgentPayloadLimits
{
    public const int MaxMessageChars = 4000;
    public const int MaxTotalPayloadChars = 14_000;

    public const int MaxSetupCvChars = 2800;
    public const int MaxSetupJobTextChars = 3800;
    public const int MaxSetupJobUrlChars = 400;
    public const int MaxSetupTitleChars = 200;
    public const int MaxSetupCompanyChars = 200;
    public const int MaxSetupInterviewAliasChars = 80;

    public static CareerToolSetup? TruncateCareerSetup(CareerToolSetup? setup)
    {
        if (setup is null)
            return null;

        return setup with
        {
            CvText = Clamp(setup.CvText, MaxSetupCvChars),
            JobText = Clamp(setup.JobText, MaxSetupJobTextChars),
            JobUrl = Clamp(setup.JobUrl, MaxSetupJobUrlChars),
            JobTitle = Clamp(setup.JobTitle, MaxSetupTitleChars),
            CompanyName = Clamp(setup.CompanyName, MaxSetupCompanyChars),
            InterviewLanguageCode = Clamp(setup.InterviewLanguageCode, 8),
            InterviewAlias = Clamp(setup.InterviewAlias, MaxSetupInterviewAliasChars),
        };
    }

    /// <summary>Gesamtgröße Message + Setup; null wenn ok, sonst Fehlercode für API.</summary>
    public static string? ValidateTotalPayload(AgentRequest request)
    {
        var msgLen = request.Message?.Length ?? 0;
        if (msgLen > MaxMessageChars)
            return "message_too_long";

        var setup = request.CareerToolSetup;
        if (setup is null)
            return msgLen > MaxTotalPayloadChars ? "payload_too_large" : null;

        var extra = (setup.CvText?.Length ?? 0)
                    + (setup.JobText?.Length ?? 0)
                    + (setup.JobUrl?.Length ?? 0)
                    + (setup.JobTitle?.Length ?? 0)
                    + (setup.CompanyName?.Length ?? 0)
                    + (setup.InterviewAlias?.Length ?? 0);

        return msgLen + extra > MaxTotalPayloadChars ? "payload_too_large" : null;
    }

    private static string? Clamp(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var t = s.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
