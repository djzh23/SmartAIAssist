using SmartAssistApi.Services;

namespace SmartAssistApi.Tests;

public class JobAnalysisSnapshotExtractorTests
{
    [Fact]
    public void TryBuild_CompleteAnalysis_ProducesCompactSnapshot()
    {
        const string reply = """
            ## Bewertung — MÖGLICH
            Passung ist solide; SAP fehlt teilweise.

            ## Muss-Kriterien
            - **C#** → ✓
            - **SAP** → ✗

            ## Keyword-Analyse
            | Keyword | Im Profil | Im CV | Priorität | Empfehlung |
            | C# | ✓ | ✓ | Hoch | Beibehalten |
            | SAP | ✗ | ✗ | Hoch | Einarbeitung thematisieren |

            ## Lücken-Analyse
            **SAP-Module**: fehlen — Verhandelbar — Erkläre Transfer von Backend-Erfahrung.

            ## Sofort-Aktionsplan
            1. Anschreiben: SAP-Story formulieren.
            2. LinkedIn: Keywords ergänzen.
            3. Netzwerk: nach SAP-Rollen fragen.

            ## Entscheidungshilfe
            Test.

            [DECISION_PROMPT]: MÖGLICH
            """;

        var s = JobAnalysisSnapshotExtractor.TryBuild(reply);

        Assert.NotNull(s);
        Assert.Contains("DECISION:", s);
        Assert.Contains("MÖGLICH", s);
        Assert.Contains("BEWERTUNG:", s);
        Assert.True(s!.Length <= JobAnalysisSnapshotExtractor.MaxChars);
    }

    [Fact]
    public void TryBuild_IncompleteSections_ReturnsNull()
    {
        var reply = "## Bewertung — STARK\n\nNur das.";
        Assert.Null(JobAnalysisSnapshotExtractor.TryBuild(reply));
    }
}
