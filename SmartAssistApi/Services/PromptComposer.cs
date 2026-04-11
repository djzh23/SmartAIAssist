using System.Text;
using Microsoft.Extensions.Caching.Memory;
using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

/// <summary>
/// Baut den gecachten System-Prefix: Kernpersönlichkeit, optional Profil, bestehende Tool-Regeln aus
/// <see cref="SystemPromptBuilder"/>, Qualitätsregeln. Profil-Änderungen invalidieren über Redis-Version.
/// </summary>
public class PromptComposer(
    CareerProfileService careerProfileService,
    SystemPromptBuilder promptBuilder,
    IMemoryCache memoryCache,
    ILogger<PromptComposer> logger)
{
    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromMinutes(5);

    public async Task<SystemPromptParts> ComposePromptPartsAsync(
        string? careerProfileUserId,
        AgentRequest request,
        SessionContext context,
        CancellationToken cancellationToken = default)
    {
        var toolType = string.IsNullOrWhiteSpace(request.ToolType) ? "general" : request.ToolType.ToLowerInvariant();
        var baseParts = promptBuilder.BuildPromptParts(toolType, context, request);
        var augmentedCached = await GetOrBuildAugmentedCachedPrefixAsync(
            careerProfileUserId,
            request.ProfileToggles,
            baseParts.CachedPrefix,
            toolType,
            cancellationToken).ConfigureAwait(false);
        return baseParts with { CachedPrefix = augmentedCached };
    }

    private async Task<string> GetOrBuildAugmentedCachedPrefixAsync(
        string? careerProfileUserId,
        ProfileContextToggles? toggles,
        string toolCachedPrefix,
        string toolType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(careerProfileUserId))
            return BuildAugmentedCachedPrefixNoCache(null, toolCachedPrefix);

        var toggleHash = ComputeToggleHash(toggles);
        var version = await careerProfileService.GetProfileCacheVersionAsync(careerProfileUserId, cancellationToken)
            .ConfigureAwait(false);
        var memKey = $"sys_prompt:{careerProfileUserId}:{toolType}:{toggleHash}:v{version}";
        if (memoryCache.TryGetValue(memKey, out string? memHit) && !string.IsNullOrEmpty(memHit))
            return memHit;

        var redisKey = $"prompt_cache:{careerProfileUserId}:{toolType}:{toggleHash}:{version}";
        try
        {
            var redisHit = await careerProfileService.TryGetPromptCacheAsync(redisKey, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(redisHit))
            {
                memoryCache.Set(memKey, redisHit, MemoryCacheTtl);
                return redisHit;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prompt Redis read failed for user {UserId}", careerProfileUserId);
        }

        string? profileBlock = null;
        if (toggles is not null)
        {
            var profile = await careerProfileService.GetProfile(careerProfileUserId).ConfigureAwait(false);
            if (profile is not null)
            {
                var ctx = careerProfileService.BuildProfileContext(profile, toggles);
                if (!string.IsNullOrWhiteSpace(ctx))
                    profileBlock = ctx.Trim();
            }
        }

        var composed = BuildAugmentedCachedPrefixNoCache(profileBlock, toolCachedPrefix);

        try
        {
            await careerProfileService
                .SetPromptCacheAsync(redisKey, composed, (int)MemoryCacheTtl.TotalSeconds, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prompt Redis write skipped for user {UserId}", careerProfileUserId);
        }

        memoryCache.Set(memKey, composed, MemoryCacheTtl);
        return composed;
    }

    private static string BuildAugmentedCachedPrefixNoCache(string? profileBlock, string toolCachedPrefix)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CorePersonality.Trim());
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(profileBlock))
        {
            sb.AppendLine(profileBlock);
            sb.AppendLine();
            sb.AppendLine(ProfileUsageInstruction.Trim());
            sb.AppendLine();
        }

        sb.AppendLine(toolCachedPrefix.Trim());
        sb.AppendLine();
        sb.AppendLine(QualityRules.Trim());
        return sb.ToString().Trim();
    }

    public static string ComputeToggleHash(ProfileContextToggles? toggles)
    {
        if (toggles is null)
            return "none";
        return $"{(toggles.IncludeBasicProfile ? 1 : 0)}{(toggles.IncludeSkills ? 1 : 0)}{(toggles.IncludeExperience ? 1 : 0)}{(toggles.IncludeCv ? 1 : 0)}_{toggles.ActiveTargetJobId ?? "x"}";
    }

    private const string CorePersonality = """
        Du bist ein erfahrener Karriereberater mit vielen Jahren Erfahrung in Personalberatung und Coaching. Du sprichst Deutsch, direkt und ehrlich — wie ein Mentor, nicht wie ein Lehrbuch.

        Dein Stil:
        - Du sprichst den Nutzer mit "du" an.
        - Du gibst konkrete, umsetzbare Ratschläge — keine generischen Floskeln.
        - Du bist ehrlich über Schwächen und Lücken — aber konstruktiv.
        - Du antwortest so kurz wie möglich, so lang wie nötig.
        """;

    private const string ProfileUsageInstruction = """
        PROFIL-NUTZUNG:
        - Beziehe dich natürlich auf die Profildaten, als würdest du die Person kennen.
        - Vermeide robotische Einleitungen wie "Laut deinem Profil…" oder "Ich sehe, dass du…".
        - Stattdessen natürlich einbinden, z. B. "Mit deiner Erfahrung in …" oder "Auf deinem Level …".
        - Vergleiche Stellenanforderungen direkt mit Skills und Erfahrung aus dem Profil.
        - Wenn nötige Infos fehlen, gezielt nachfragen — nur wenn es für die Antwort wirklich nötig ist.
        """;

    private const string QualityRules = """
        ANTWORT-REGELN:
        - Beginne direkt mit dem Inhalt. Kein "Natürlich!", "Gerne!", "Klar!".
        - Wiederhole nicht wörtlich, was der Nutzer gerade geschrieben hat.
        - Keine Füllsätze wie "Das ist eine gute Frage" oder "Es gibt viele Möglichkeiten".
        - Wenn du im Verlauf schon etwas Gesagtes vertiefen sollst: baue darauf auf, wiederhole keine identischen Formulierungen.
        - Markdown: ## Überschriften, **fett** für Schlüsselbegriffe, > für Beispiel-Formulierungen; Tabellen (| … |) für Vergleiche, wo es hilft.
        - Halte dich unter 250 Wörtern, außer der Nutzer verlangt ausdrücklich mehr Detail.
        - Schließe mit einem konkreten nächsten Schritt — nicht mit einer offenen Liste von Optionen.
        """;
}
