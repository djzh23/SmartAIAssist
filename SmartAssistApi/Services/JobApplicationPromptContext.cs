using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Formats the compact German job-application block for the LLM system prompt.</summary>
public static class JobApplicationPromptContext
{
    public static string? Build(JobApplicationDocument? app)
    {
        if (app is null)
            return null;

        var jd = (app.JobDescription ?? string.Empty).Trim();
        if (jd.Length > 2400)
            jd = jd[..2400] + "…";

        var parts = new List<string>
        {
            "[AKTUELLE BEWERBUNG — nur diese Stelle adressieren, keine anderen Annahmen]",
            $"Bewerbungs-ID: {app.Id}",
            $"Rolle: {app.JobTitle}",
            $"Firma: {app.Company}",
            $"Status: {app.Status}",
        };

        if (!string.IsNullOrWhiteSpace(app.JobUrl))
            parts.Add($"URL: {app.JobUrl}");

        if (jd.Length > 0)
            parts.Add($"Stellenbeschreibung (Auszug):\n{jd}");

        if (!string.IsNullOrWhiteSpace(app.CoverLetterText))
        {
            var cl = app.CoverLetterText.Trim();
            if (cl.Length > 800)
                cl = cl[..800] + "…";
            parts.Add($"Gespeichertes Anschreiben (Auszug):\n{cl}");
        }

        if (!string.IsNullOrWhiteSpace(app.InterviewNotes))
        {
            var n = app.InterviewNotes.Trim();
            if (n.Length > 600)
                n = n[..600] + "…";
            parts.Add($"Interview-Notizen:\n{n}");
        }

        parts.Add("[ENDE BEWERBUNG]");
        return string.Join("\n", parts);
    }
}
