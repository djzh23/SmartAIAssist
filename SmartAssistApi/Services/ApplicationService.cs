using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Redis-backed job applications for <c>/api/applications</c> and agent context.</summary>
public class ApplicationService(IRedisStringStore redis, ILogger<ApplicationService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string AppsKey(string userId) => $"job_apps:{userId}";

    public async Task<List<JobApplicationDocument>> ListAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new List<JobApplicationDocument>();

        try
        {
            var json = await redis.StringGetAsync(AppsKey(userId), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return new List<JobApplicationDocument>();

            var list = JsonSerializer.Deserialize<List<JobApplicationDocument>>(json, JsonOpts);
            return list ?? new List<JobApplicationDocument>();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list applications for {UserId}", userId);
            return new List<JobApplicationDocument>();
        }
    }

    public async Task<JobApplicationDocument?> GetAsync(string userId, string applicationId, CancellationToken cancellationToken = default)
    {
        var list = await ListAsync(userId, cancellationToken).ConfigureAwait(false);
        return list.FirstOrDefault(a => string.Equals(a.Id, applicationId, StringComparison.Ordinal));
    }

    public async Task SaveAllAsync(string userId, List<JobApplicationDocument> apps, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return;

        try
        {
            foreach (var a in apps)
                a.UpdatedAt = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(apps, JsonOpts);
            await redis.StringSetAsync(AppsKey(userId), json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save applications for {UserId}", userId);
            throw;
        }
    }

    /// <summary>Compact German block for system prompt when <see cref="AgentRequest.JobApplicationId"/> is set.</summary>
    public async Task<string?> BuildPromptContextAsync(string userId, string? applicationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(applicationId))
            return null;

        var app = await GetAsync(userId, applicationId, cancellationToken).ConfigureAwait(false);
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
