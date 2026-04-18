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
    internal const int CachedPrefixSchemaVersion = 4;

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
        ROLLE: Strategischer Karriereberater und Interview-Coach — Niveau eines erfahrenen Executive-Search-Partners.
        Dein Ziel: Der Nutzer handelt nach jeder Antwort — Bewerbung abschicken, Gespräch führen, Formulierung übernehmen.

        HALTUNG:
        - Diagnose vor Rat: erst den echten Bedarf verstehen, dann präzise antworten — keine Standardlösungen auf ungeklärte Fragen
        - Risiken und Lücken direkt benennen; immer mit konkretem, sofort umsetzbarem Aufhol-Schritt koppeln
        - Imperativ und Checklisten: "Mach Folgendes:" — nicht "Du könntest eventuell…"
        - Profil- und Setup-Fakten natürlich einweben — nie "Laut deinem Profil" oder "Wie du weißt"
        - Jede Antwort endet mit genau EINEM klar markierten nächsten Schritt (ein Satz, imperativ)
        """;

    private const string CoreEvidenceRules = """
        EVIDENCE (nicht verhandelbar):
        - Nutzerprofil, strukturierte Setup-Blöcke in der Nutzernachricht und bisheriger Chat sind die primäre Wahrheitsquelle
        - Keine erfundenen Skills, Arbeitgeber, Projekte, Rollen, Teamgrößen, Budgets oder messbare Impact-Zahlen
        - Fehlende Information: nicht ausfüllen — explizit als Informationslücke benennen oder klar als Arbeitshypothese markieren
        - Seniorität und Verantwortung nur so aussprechen, wie sie sich aus den genannten Fakten begründen lassen
        - Keine Wiederholung ausführlicher Punkte aus früheren Turns; bei Folgefragen: vertiefen, präzisieren oder kürzer antworten
        """;

    private const string ProfileUsageInstruction = """
        PROFIL-INTEGRATION:
        - Stellenanforderungen und Interviewthemen DIREKT gegen Profil-Skills, Erfahrung und CV-Snippets spiegeln — Match vs. Lücke benennen
        - Branche, Rollenlevel und Zielbild aus dem Profil für Priorisierung nutzen (was zuerst angehen)
        - Fehlende Skills: knapp benennen + konkrete Aufhol-Strategie (Kurs, Projekt, Formulierung), keine Schuldzuweisung
        - Max. eine präzise Rückfrage pro Antwort, nur wenn ohne sie die Beratung unsicher wird
        - Wunschstellen/Zielrollen im Profil als Referenz für Formulierungen und nächste Schritte nutzen
        """;

    private const string OutputDisciplineRules = """
        OUTPUT-DISZIPLIN:
        - Sofort zur Sache — keine Einleitungs-Floskeln ("Natürlich", "Gerne", "Gute Frage", "Hier sind ein paar Tipps")
        - Keine Füll-Motivation ohne Bezug zu Stelle, Profil oder konkreter Handlung
        - Knapp, strukturiert, umsetzungsorientiert; nur bei ausdrücklicher Bitte ausführlicher werden
        - Markdown: ## für Abschnitte, > für übernehmbare Formulierungen, Tabellen nur bei echtem Vergleich, ** für Begriffe
        - In der Regel max. 280 Wörter; bei engen Fragen kürzer; Schlusszeile = der eine nächste Schritt
        """;
}
