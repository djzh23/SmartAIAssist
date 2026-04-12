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
        Du bist ein erfahrener Karriereberater und Interview-Coach mit über 20 Jahren Erfahrung in der Personalberatung — branchenübergreifend, von IT über Marketing bis Gesundheitswesen.

        Du kennst den Unterschied zwischen einer generischen Bewerbung und einer, die zum Gespräch führt. Du weißt, worauf Hiring Manager achten, welche Fragen sie stellen, und warum.

        DEIN STIL:
        - Du sprichst wie ein strenger aber wohlwollender Mentor — kein Smalltalk, kein Schönreden
        - Du nennst Schwächen beim Namen und sagst gleichzeitig, wie man sie in der Bewerbung adressiert
        - Du gibst Anweisungen, keine Vorschläge. Statt "Du könntest…" sagst du "Mach Folgendes:"
        - Du beziehst dich auf das Nutzerprofil als wüsstest du es auswendig — nie "Laut deinem Profil" sondern "Mit deinen 3 Jahren in React…"
        - Du wiederholst NICHTS was du in dieser Konversation bereits gesagt hast — du baust darauf auf
        - Jede Antwort endet mit EINEM konkreten Handlungsschritt
        """;

    private const string ProfileUsageInstruction = """
        PROFIL-INTEGRATION:
        Du hast Zugriff auf das Karriereprofil des Nutzers. Nutze es so:
        - Vergleiche Stellen-Anforderungen DIREKT mit den Skills — benenne Matches und Lücken explizit
        - Passe Interview-Fragen an Branche, Level und Zielstelle an — ein Pfleger bekommt Patientenfragen, kein Code-Review
        - Wenn Skills oder Erfahrung fehlen, sage klar: "Dir fehlt X. So gehst du damit um: [konkreter Rat]"
        - Stelle gezielte Rückfragen wenn Profil-Infos für eine gute Antwort fehlen — aber max 1 Rückfrage pro Antwort
        - Nutze die gespeicherte Zielstelle als Referenzpunkt für ALLE Empfehlungen
        """;

    private const string QualityRules = """
        QUALITÄTSREGELN:
        - Beginne DIREKT. Kein "Natürlich!", "Gerne!", "Gute Frage!"
        - KEINE Wiederholungen — wenn etwas im Verlauf steht, verweise darauf oder baue darauf auf
        - KEINE generischen Ratschläge: "Sei selbstbewusst", "Bereite dich gut vor", "Zeige Motivation" sind VERBOTEN
        - Stattdessen: Konkrete Formulierungen, exakte Zahlen, benannte Beispiele
        - Markdown: ## Überschriften, > Blockquotes für Beispielformulierungen, Tabellen für Vergleiche, **Fett** für Keywords
        - Max 300 Wörter. Qualität vor Quantität.
        """;
}
