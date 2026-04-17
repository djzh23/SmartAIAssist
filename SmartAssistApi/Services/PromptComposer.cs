using System.Collections.Generic;
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
    LearningMemoryService learningMemoryService,
    IApplicationService applicationService,
    IMemoryCache memoryCache,
    ILogger<PromptComposer> logger)
{
    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>Bump when core/cached prefix text changes so Redis prompt cache invalidates without profile edits.</summary>
    internal const int CachedPrefixSchemaVersion = 2;

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
        var withCache = baseParts with { CachedPrefix = augmentedCached };
        return await AppendLearningInsightsAsync(
                request.ConversationScopeUserId,
                request.JobApplicationId,
                withCache,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<SystemPromptParts> AppendLearningInsightsAsync(
        string? conversationScopeUserId,
        string? jobApplicationId,
        SystemPromptParts parts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(conversationScopeUserId))
            return parts;

        try
        {
            string? jobBlock = null;
            try
            {
                jobBlock = await applicationService
                    .BuildPromptContextAsync(conversationScopeUserId, jobApplicationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Job application context skipped for user {UserId}", conversationScopeUserId);
            }

            var memory = await learningMemoryService.GetMemory(conversationScopeUserId, cancellationToken)
                .ConfigureAwait(false);
            var block = learningMemoryService.BuildInsightsContext(memory, jobApplicationId);
            var merged = new List<string>();
            if (!string.IsNullOrWhiteSpace(jobBlock))
                merged.Add(jobBlock);
            if (!string.IsNullOrWhiteSpace(block))
                merged.Add(block);
            if (merged.Count == 0)
                return parts;

            var combined = string.Join("\n\n", merged);
            var d = parts.DynamicToolSuffix ?? string.Empty;
            var newDynamic = string.IsNullOrEmpty(d) ? combined : $"{combined}\n\n{d}";
            return parts with { DynamicToolSuffix = newDynamic };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Learning insights block skipped for user {UserId}", conversationScopeUserId);
            return parts;
        }
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
        var memKey = $"sys_prompt:{careerProfileUserId}:{toolType}:{toggleHash}:v{version}:s{CachedPrefixSchemaVersion}";
        if (memoryCache.TryGetValue(memKey, out string? memHit) && !string.IsNullOrEmpty(memHit))
            return memHit;

        var redisKey = $"prompt_cache:{careerProfileUserId}:{toolType}:{toggleHash}:{version}:s{CachedPrefixSchemaVersion}";
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
        sb.AppendLine(CoreVoice.Trim());
        sb.AppendLine();
        sb.AppendLine(CoreEvidenceRules.Trim());
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
        sb.AppendLine(OutputDisciplineRules.Trim());
        return sb.ToString().Trim();
    }

    public static string ComputeToggleHash(ProfileContextToggles? toggles)
    {
        if (toggles is null)
            return "none";
        return $"{(toggles.IncludeBasicProfile ? 1 : 0)}{(toggles.IncludeSkills ? 1 : 0)}{(toggles.IncludeExperience ? 1 : 0)}{(toggles.IncludeCv ? 1 : 0)}_{toggles.ActiveTargetJobId ?? "x"}";
    }

    private const string CoreVoice = """
        ROLLE: Erfahrener Karriereberater und Interview-Coach — sachlich, direkt, ohne Marketing-Sprech.

        STIL:
        - Schwächen klar benennen und sofort mit umsetzbarem Fix verbinden
        - Anweisungen statt vager Optionen ("Mach Folgendes:", nicht "Du könntest…")
        - Profil natürlich einweben — nie Meta-Formulierungen wie "Laut deinem Profil"
        - Jede Antwort endet mit genau EINEM konkreten nächsten Schritt
        """;

    private const string CoreEvidenceRules = """
        EVIDENCE (nicht verhandelbar):
        - Nutzerprofil, Setup-Daten in der Nutzernachricht und bisheriger Chat sind die primäre Wahrheitsquelle
        - Keine erfundenen Skills, Arbeitgeber, Projekte, Teamgrößen, Produktionsverantwortung oder messbare Impact-Zahlen
        - Wenn etwas nicht explizit genannt ist: nicht behaupten — als Lücke benennen oder klar als Annahme kennzeichnen ("Unter der Annahme, dass …")
        - Keine Übertreibung des Senioritätslevels; Formulierungen müssen zum erkennbaren Erfahrungsstand passen
        - Keine Wiederholung bereits gelieferter Punkte aus früheren Turns; vertiefen oder schmaler werden, außer der User verlangt Wiederholung
        """;

    private const string ProfileUsageInstruction = """
        PROFIL-INTEGRATION:
        - Anforderungen der Stelle DIREKT mit Profil-Skills abgleichen — Matches und Lücken explizit benennen
        - Interviewfragen an Branche, Level und Zielstelle anpassen
        - Fehlende Skills: "Dir fehlt X. So gehst du damit um: …" (konkret)
        - Max. eine gezielte Rückfrage pro Antwort, nur wenn nötig
        - Zielstelle aus dem Profil als Referenz für Empfehlungen nutzen
        """;

    private const string OutputDisciplineRules = """
        OUTPUT-DISZIPLIN:
        - Direkt einsteigen — keine Floskeln ("Natürlich", "Gerne", "Gute Frage")
        - Keine generischen Motivationsphrasen ohne konkreten Bezug zur Stelle oder zum Profil
        - Dicht und praxisnah; kein Essay-Stil, außer der User fordert ausdrücklich Detailtiefe
        - Markdown: ## für Abschnitte, > für zitierbare Formulierungen, Tabellen nur bei echtem Vergleich, ** für Keywords
        - In der Regel max. 280 Wörter; kürzer bevorzugt wenn die Frage eng ist
        """;
}
