using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>Job applications in Redis. Key: applications:{userId} (no TTL).</summary>
public sealed class ApplicationService(IRedisStringStore redis, ILogger<ApplicationService> logger)
{
    private const int MaxApplications = 30;
    private const int JobDescriptionMax = 3000;
    private const int CoverLetterMax = 5000;
    private const int InterviewNotesMax = 3000;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static string ApplicationsKey(string userId) => $"applications:{userId}";

    public async Task<List<JobApplication>> GetApplications(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return [];

        var raw = await redis.GetAsync(ApplicationsKey(userId), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            var list = JsonSerializer.Deserialize<List<JobApplication>>(raw, JsonOpts);
            if (list is null)
                return [];
            return list.OrderByDescending(a => a.UpdatedAt).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deserialize applications for user {UserId}", userId);
            return [];
        }
    }

    private async Task SaveApplications(string userId, List<JobApplication> apps, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(apps, JsonOpts);
        await redis.SetAsync(ApplicationsKey(userId), json, ttlSeconds: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JobApplication> CreateApplication(
        string userId,
        string jobTitle,
        string company,
        string? jobUrl,
        string? jobDescription,
        CancellationToken cancellationToken = default)
    {
        var apps = await GetApplications(userId, cancellationToken).ConfigureAwait(false);
        if (apps.Count >= MaxApplications)
            throw new InvalidOperationException("Maximal 30 Bewerbungen. Archiviere oder lösche abgeschlossene Bewerbungen.");

        var desc = jobDescription is null
            ? null
            : (jobDescription.Length > JobDescriptionMax ? jobDescription[..JobDescriptionMax] : jobDescription);

        var app = new JobApplication
        {
            JobTitle = jobTitle.Trim(),
            Company = company.Trim(),
            JobUrl = string.IsNullOrWhiteSpace(jobUrl) ? null : jobUrl.Trim(),
            JobDescription = string.IsNullOrWhiteSpace(desc) ? null : desc,
            Timeline =
            [
                new ApplicationEvent { Description = "Bewerbung angelegt" },
            ],
        };

        apps.Insert(0, app);
        await SaveApplications(userId, apps, cancellationToken).ConfigureAwait(false);
        return app;
    }

    public async Task UpdateStatus(
        string userId,
        string applicationId,
        ApplicationStatus newStatus,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var apps = await GetApplications(userId, cancellationToken).ConfigureAwait(false);
        var app = apps.FirstOrDefault(a => a.Id == applicationId)
            ?? throw new KeyNotFoundException("Bewerbung nicht gefunden.");

        app.Status = newStatus;
        app.StatusUpdatedAt = DateTime.UtcNow;
        app.UpdatedAt = DateTime.UtcNow;

        var statusLabel = newStatus switch
        {
            ApplicationStatus.Applied => "Bewerbung abgeschickt",
            ApplicationStatus.PhoneScreen => "Erstgespräch geplant",
            ApplicationStatus.Interview => "Vorstellungsgespräch",
            ApplicationStatus.Assessment => "Test-Aufgabe / Assessment",
            ApplicationStatus.Offer => "Angebot erhalten",
            ApplicationStatus.Accepted => "Stelle angenommen",
            ApplicationStatus.Rejected => "Absage erhalten",
            ApplicationStatus.Withdrawn => "Bewerbung zurückgezogen",
            _ => "Status geändert",
        };

        app.Timeline.Add(new ApplicationEvent
        {
            Description = statusLabel,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        });

        await SaveApplications(userId, apps, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveCoverLetter(
        string userId,
        string applicationId,
        string coverLetterText,
        CancellationToken cancellationToken = default)
    {
        var apps = await GetApplications(userId, cancellationToken).ConfigureAwait(false);
        var app = apps.FirstOrDefault(a => a.Id == applicationId);
        if (app is null)
            return;

        app.CoverLetterText = coverLetterText.Length > CoverLetterMax
            ? coverLetterText[..CoverLetterMax]
            : coverLetterText;
        app.UpdatedAt = DateTime.UtcNow;
        app.Timeline.Add(new ApplicationEvent { Description = "Anschreiben gespeichert" });
        await SaveApplications(userId, apps, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveInterviewNotes(
        string userId,
        string applicationId,
        string notes,
        CancellationToken cancellationToken = default)
    {
        var apps = await GetApplications(userId, cancellationToken).ConfigureAwait(false);
        var app = apps.FirstOrDefault(a => a.Id == applicationId);
        if (app is null)
            return;

        app.InterviewNotes = notes.Length > InterviewNotesMax ? notes[..InterviewNotesMax] : notes;
        app.UpdatedAt = DateTime.UtcNow;
        app.Timeline.Add(new ApplicationEvent { Description = "Interview-Notizen gespeichert" });
        await SaveApplications(userId, apps, cancellationToken).ConfigureAwait(false);
    }

    public async Task LinkChatSession(
        string userId,
        string applicationId,
        string sessionType,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var apps = await GetApplications(userId, cancellationToken).ConfigureAwait(false);
        var app = apps.FirstOrDefault(a => a.Id == applicationId);
        if (app is null)
            return;

        var st = sessionType.Trim().ToLowerInvariant();
        if (st == "analysis")
            app.AnalysisSessionId = sessionId;
        if (st == "interview")
            app.InterviewSessionId = sessionId;

        app.UpdatedAt = DateTime.UtcNow;
        await SaveApplications(userId, apps, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteApplication(string userId, string applicationId, CancellationToken cancellationToken = default)
    {
        var apps = await GetApplications(userId, cancellationToken).ConfigureAwait(false);
        apps.RemoveAll(a => a.Id == applicationId);
        await SaveApplications(userId, apps, cancellationToken).ConfigureAwait(false);
    }
}
