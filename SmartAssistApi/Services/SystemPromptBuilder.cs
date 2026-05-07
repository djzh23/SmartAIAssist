using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public class SystemPromptBuilder
{
    private const int ApproxCharsPerToken = 4;

    /// <summary>Below this size, prompt-cache hit rate may be low; we keep prompts intentionally short (Groq TPM, cost).</summary>
    public const int MinRecommendedCachedPrefixTokens = 128;

    public static int ApproximateTokenCount(string text) =>
        string.IsNullOrEmpty(text) ? 0 : Math.Max(1, text.Length / ApproxCharsPerToken);

    public string BuildPrompt(string toolType, SessionContext context, AgentRequest request) =>
        BuildPromptParts(toolType, context, request).ToCombinedPrompt();

    /// <summary>
    /// Builds cached vs variable system prompt segments. <see cref="SystemPromptParts.CachedPrefix"/> must stay stable across
    /// turns for the same tool type (except when tool config like language pair changes — then cache miss is expected).
    /// </summary>
    public SystemPromptParts BuildPromptParts(string toolType, SessionContext context, AgentRequest request)
    {
        var normalizedTool = string.IsNullOrWhiteSpace(toolType) ? "general" : toolType.ToLowerInvariant();
        var languageRule = LanguageRuleFor(normalizedTool, context);

        if (string.IsNullOrWhiteSpace(languageRule))
        {
            throw new InvalidOperationException(
                $"Language rule resolved empty for tool type '{normalizedTool}'. Refusing to build system prompt.");
        }

        var parts = normalizedTool switch
        {
            "jobanalyzer" => BuildJobAnalyzerParts(context, languageRule),
            "interviewprep" => BuildInterviewPrepParts(context, languageRule),
            "programming" => BuildProgrammingParts(context, languageRule),
            "language" => BuildLanguageToolParts(request, languageRule),
            "cover_letter" => new SystemPromptParts(CoverLetterSkillPrompt, string.Empty, languageRule),
            "salary_coach" => new SystemPromptParts(SalaryCoachSkillPrompt, string.Empty, languageRule),
            "linkedin_optimizer" => new SystemPromptParts(LinkedInSkillPrompt, string.Empty, languageRule),
            _ => new SystemPromptParts(BuildGeneralPrompt(), string.Empty, languageRule),
        };

        if (string.IsNullOrWhiteSpace(parts.CachedPrefix))
        {
            throw new InvalidOperationException(
                $"Cached system prefix is empty for tool type '{normalizedTool}'. This is a bug in SystemPromptBuilder.");
        }

        return parts;
    }

    private static SystemPromptParts BuildLanguageToolParts(AgentRequest request, string languageRule)
    {
        var cached = BuildLanguagePrompt(request);
        return new SystemPromptParts(cached, string.Empty, languageRule);
    }

    private static SystemPromptParts BuildProgrammingParts(SessionContext context, string languageRule)
    {
        var lang = context.ProgrammingLanguage ?? "beliebig";
        var codeContext = string.IsNullOrEmpty(context.CurrentCodeContext)
            ? "Noch kein Code geteilt."
            : string.Concat(
                "Aktueller Code:\n```\n",
                context.CurrentCodeContext,
                "\n```");

        var cached = """
            Du bist erfahrener Software-Entwickler. Erkläre in der Sprache aus den Konversationsregeln (zweiter System-Block unten).
            Code in der vom Nutzer erwarteten Sprache; Markdown-Codeblöcke mit Sprach-Tag.

            Ablauf: zuerst 1–2 Sätze Problem und Lösungsidee, dann Code, dann nur nötige Zeilen-Hinweise.
            Debugging: Symptom, Ursache, Fix. Code-Review: knapp was passt, was verbessern.
            Kommentare im Code sparsam. Kein Boilerplate wenn ein Snippet reicht.

            """;

        var dynamicHead = $"""
            PROGRAMMIER-KONTEXT: Sprache/Stack: {lang}

            """;

        var dynamicTool = string.Concat(dynamicHead, codeContext);

        return new SystemPromptParts(cached, dynamicTool, languageRule);
    }

    private static SystemPromptParts BuildInterviewPrepParts(SessionContext context, string languageRule)
    {
        var hasJob = context.InterviewJobTitle != null;
        var hasCv = !string.IsNullOrEmpty(context.UserCV);

        var cached = $"""
            {InterviewToolContractHeader}

            MODUS 1 — User fragt nach Fragen / Vorbereitung:
            - Max. 5 Fragen; Mix: 2 fachlich (Branche), 2 Verhalten, 1 Stress
            - Pro Frage: ### Frage N, **Intention** (1 Satz), STAR-Leitfaden (4 Bullet), > ein Satz Beispiel-Einstieg (nur mit Profil-/Setup-Fakten), **Warnsignale**

            MODUS 2 — User antwortet auf eine Interviewfrage:
            - Struktur, Konkretheit, Warum-Faktor, Abschweifen, Score ★…★, **Verbesserte Version** (umformuliert, glaubwürdig zum Level)

            OUTPUT-VERTRAG (InterviewPrep):
            - Max. 4 Markdown-##-Abschnitte (MODUS 1 oder 2 — nicht mischen)
            - Kein allgemeines Lob; nur konstruktive Kritik und nächste Schritte
            - Keine erfundenen Arbeitgeber- oder Projektgeschichten; Beispiele an echtes Profil/Setup anbinden
            - Antwortsprache: Konversationsregeln (zweiter System-Block unten)
            - Als allerletzte Zeile exakt eine Zeile: READINESS: NN/100 (NN = realistische Vorbereitung 0–100, ohne Marketing-Sprache)

            TON: Professionelles Assessment-Niveau — präzise, sachlich, ohne Beschönigung; jede Rückmeldung muss den Kandidaten
            für das echte Gespräch weiterbringen (Formulierung, Struktur, Inhalt).

            """;

        if (!hasJob && !hasCv)
        {
            var waiting = """

                Status: noch keine Rolle und kein CV.

                Bitte höflich nach (1) Zielrolle/Unternehmen und (2) CV-Text fragen; bis dahin nur allgemeine Warm-up-Fragen, max. 2.
                """;
            return new SystemPromptParts(cached, waiting, languageRule);
        }

        var jobSection = hasJob
            ? $"POSITION: {context.InterviewJobTitle} bei {context.InterviewCompany}"
            : "POSITION: noch nicht genannt";

        var cvText = context.UserCV ?? string.Empty;
        var cvSection = hasCv
            ? $"""

              CV (Auszug):
              {cvText[..Math.Min(cvText.Length, 1500)]}
              Fragen und Feedback auf echte CV-Inhalte beziehen.
              """
            : "CV: fehlt — gezielte aber generische Fragen.";

        var practiced = context.PractisedQuestions.Any()
            ? $"BEREITS GEÜBT (nicht wiederholen): {string.Join("; ", context.PractisedQuestions.TakeLast(5))}"
            : "Noch keine geübten Fragen.";

        var dynamicTail = $"""

            {jobSection}
            {cvSection}
            {practiced}

            Regeln: keine Duplikate aus „BEREITS GEÜBT“; keine Wiederholung bereits beantworteter Inhalte; konkret und umsetzbar.
            """;

        return new SystemPromptParts(cached, dynamicTail, languageRule);
    }

    private const string InterviewToolContractHeader = """
        WERKZEUG: Interview Coach — Probe-Interview

        Du agierst als erfahrener Hiring-Manager: Fragen stellen, Antworten nach Klarheit, Konkretheit und Glaubwürdigkeit bewerten.
        Schwache Antworten direkt markieren — nie höflich durchwinken; jede Rückmeldung muss das echte Gespräch besser machen.
        """;

    private static SystemPromptParts BuildJobAnalyzerParts(SessionContext context, string languageRule)
    {
        var job = context.Job;
        var hasJob = job is { IsAnalyzed: true };

        var cached = """
            WERKZEUG: Stellenanalyse — Strategische Passungsbewertung

            Du agierst wie ein erfahrener Talent-Acquisition-Lead / HR-Business-Partner: ehrliche Einschätzung von Passung und Risiken,
            ohne Marketing-Sprache; der Nutzer soll Bewerbung und Gespräch strategisch vorbereiten können.
            Antwortsprache: Konversationsregeln (zweiter System-Block unten).

            PFLICHT-ABSCHNITTE — nur MODUS A (Erstanalyse), Reihenfolge, je ##-Überschrift:
            ## Bewertung — STARK / MÖGLICH / SCHWIERIG
               Kriterien (keine Note-Inflation): STARK = ≥80% der MUSS-Kriterien erfüllt UND mindestens 6/10 ATS-Keywords abgedeckt.
               MÖGLICH = 50–79% MUSS-Kriterien ODER kritische Lücken die im Anschreiben adressierbar sind.
               SCHWIERIG = <50% MUSS-Kriterien ODER fundamentale Qualifikationslücken. „STARK“ ist nicht der Default — die meisten Analysen sind „MÖGLICH“.
            ## Muss-Kriterien — jede harte Anforderung als Bullet: **Anforderung** → ✓ / ✗ / ⚠ mit konkretem Profil-/CV-Bezug (keine vagen Behauptungen)
            ## Keyword-Analyse — Tabelle mit EXAKT diesen Spalten:
               | Keyword | Im Profil | Im CV | Priorität (Hoch/Mittel/Niedrig) | Empfehlung |
               Hoch = in der Anzeige prominent oder unter MUSS; Mittel = Aufgaben/Soll; Niedrig = Nice-to-have oder Kontext.
               Mindestens 8, maximal 12 Keywords; sortiert nach Priorität absteigend.
            ## Lücken-Analyse — pro Lücke ein Block:
               **[Lücke]**, **Kritikalität** (Deal-Breaker / Verhandelbar / Nebensache), **Anschreiben-Strategie** (ein konkreter Satz), **Aufhol-Option** (Maßnahme + grober Zeitrahmen).
            ## Sofort-Aktionsplan — genau 3 nummerierte, imperative Schritte mit Platzhaltern in [Klammern] wo nötig
            ## Entscheidungshilfe — eine klare Empfehlung (ein Absatz) passend zur Bewertung; danach IMMER exakt eine Zeile:
               [DECISION_PROMPT]: STARK|MÖGLICH|SCHWIERIG

            OUTPUT-VERTRAG (JobAnalyzer):

            MODUS A — ERSTANALYSE (noch keine abgeschlossene Erstanalyse in dieser Session — siehe dynamischer Block „AKTUELLER MODUS“ unten):
            - Genau die sechs ##-Abschnitte oben in exakter Reihenfolge; vollständig, strukturiert, mit Tabellen und Bullets wo angegeben
            - Keine erfundenen Profil-Fakten; fehlende Daten als Lücke benennen („Keine Information über [X] im Profil — bitte ergänzen“)
            - Umfang für Modus A üblicherweise etwa 400–700 Wörter
            - Abschlusszeile [DECISION_PROMPT] ist Pflicht

            MODUS B — FOLGEFRAGE (Erstanalyse bereits abgeschlossen — „AKTUELLER MODUS: B“ und ein ANALYSE-SNAPSHOT im dynamischen Block unten):
            - KEINE erneuten ##-Pflichtabschnitte der Erstanalyse und KEIN [DECISION_PROMPT]
            - Antwort DIREKT auf die Frage — knapp, konkret, umsetzbar; max. etwa 200 Wörter
            - Nutze den ANALYSE-SNAPSHOT als Referenz für Bewertung, Lücken und Keywords; wiederhole die volle Analyse NICHT
            - Format: kurze Absätze; für übernehmbare Formulierungen Markdown-Blockquotes (>)
            - Bei Formulierungshilfen: konkreten Absatz zum Kopieren liefern
            - Bei „Lohnt es sich?“: 2–4 Sätze ehrliche Einschätzung aus der Erstanalyse ableiten
            - Bei Off-Topic: freundlich zur konkreten Stelle zurücklenken

            MODUS-ERKENNUNG:
            - Modus A wenn der dynamische Block „AKTUELLER MODUS: A“ anzeigt (keine Snapshot-Daten)
            - Modus B wenn „AKTUELLER MODUS: B“ und ein ANALYSE-SNAPSHOT vorhanden ist
            - Im Zweifel Modus B wählen, wenn ein Snapshot existiert; sonst Modus A nur wenn eine Erstanalyse noch aussteht

            FOLLOW-UP-ANTI-PATTERNS (VERBOTEN in Modus B):
            - Meta-Floskeln wie „Basierend auf meiner Analyse…“ — einfach antworten
            - Erneute komplette Muss-Liste wenn nur nach einem Kriterium gefragt wurde
            - Erneute Keyword-Tabelle wenn nur nach einem Keyword gefragt wurde
            - „Wie bereits erwähnt…“ — redundant wenn Snapshot existiert
            - Neuen vollständigen Aktionsplan wenn nicht danach gefragt wurde
            - Allgemeine Karrieretipps ohne Bezug zu dieser Stelle/Firma

            FOLLOW-UP-BEST-PRACTICES (ERWÜNSCHT in Modus B):
            - Direktantwort in den ersten 1–2 Sätzen, dann optional Vertiefung
            - Kopierbare Formulierungen als > Blockquote
            - Konkreter Bezug zu Firma/Rolle/Anforderungen aus Snapshot und aktiver Stelle

            """;

        if (!hasJob)
        {
            var waiting = """

                Status: noch keine Stellenanzeige.

                Bitte Nutzer:in bitten, die vollständige Anzeige einzufügen (oder Link — dann nach Inhalt fragen).
                Erst danach Analyse; vorher keine Detailfragen zur Stelle.
                """;
            return new SystemPromptParts(cached, waiting, languageRule);
        }

        var cvSection = string.IsNullOrEmpty(context.UserCV)
            ? """

              Hinweis: kein CV — bei CV-Fragen nach Text fragen.
              """
            : $"""

              CV des Nutzers (Abgleich mit Anforderungen):
              {context.UserCV[..Math.Min(context.UserCV.Length, 2000)]}
              """;

        var snapshotSection = context.HasCompletedAnalysis && !string.IsNullOrWhiteSpace(context.AnalysisSnapshot)
            ? $"""

                [ANALYSE-SNAPSHOT — ERGEBNISSE DER ERSTANALYSE]
                {context.AnalysisSnapshot.Trim()}
                [ENDE ANALYSE-SNAPSHOT]

                AKTUELLER MODUS: B (Follow-Up). Nutze den Snapshot als Referenz. Wiederhole NICHT die volle Analyse.
                """
            : """

                AKTUELLER MODUS: A (Erstanalyse). Liefere die vollständige Analyse mit allen sechs Pflichtabschnitten inkl. Entscheidungshilfe und Zeile [DECISION_PROMPT], sobald du die erste vollständige Passungsanalyse ausführst.
                """;

        var dynamicTail = $"""

            AKTIVE STELLE (immer verwenden):
            Position: {job!.JobTitle}
            Unternehmen: {job.CompanyName}
            Ort: {job.Location}

            Anforderungen:
            {string.Join("\n", job.KeyRequirements.Select(r => $"- {r}"))}

            ATS-Keywords:
            {string.Join(", ", job.Keywords)}

            Anzeigentext (Auszug):
            {job.RawJobText[..Math.Min(job.RawJobText.Length, 1500)]}

            {cvSection}

            Nutzer-Hintergrund:
            {(context.UserFacts.Any()
                ? string.Join("\n", context.UserFacts.Select(f => $"- {f}"))
                : "noch unbekannt")}

            Regeln: nicht erneut nach „welche Stelle“ fragen; konkret auf Firma/Rolle und Listen beziehen; Off-Topic freundlich zurücklenken.

            {snapshotSection}
            """;

        return new SystemPromptParts(cached, dynamicTail, languageRule);
    }

    private static string LanguageRuleFor(string toolType, SessionContext context) =>
        toolType == "language"
            ? BuildLanguageToolConversationInstruction(context)
            : BuildConversationLanguageInstruction(context);

    private static string BuildConversationLanguageInstruction(SessionContext context)
    {
        var lang = string.Equals(context.ConversationLanguage, "en", StringComparison.OrdinalIgnoreCase)
            ? "en"
            : "de";

        return lang == "de"
            ? """
              LANGUAGE MODE:
              - Always answer in German.
              - Keep this language until the user clearly switches to English.
              - If the user switches to English, continue in English in following turns.
              """
            : """
              LANGUAGE MODE:
              - Always answer in English.
              - Keep this language until the user clearly switches to German.
              - If the user switches to German, continue in German in following turns.
              """;
    }

    private static string BuildLanguageToolConversationInstruction(SessionContext context)
    {
        var lang = string.Equals(context.ConversationLanguage, "en", StringComparison.OrdinalIgnoreCase)
            ? "English"
            : "German";

        return $"""
            CONVERSATION LANGUAGE CONTEXT:
            - For explanations and clarifications, prefer {lang}.
            - For translation output, follow the target language requested by the user.
            """;
    }

    private const string CoverLetterSkillPrompt = """
        WERKZEUG: Anschreiben-Generator

        STRUKTUR (Reihenfolge einhalten):
        1. **Betreffzeile**: Stelle + Referenznummer (falls vorhanden)
        2. **Hook-Einleitung** (2 Sätze): ein spezifisches Detail zum Unternehmen oder zur Ausschreibung + warum genau diese Stelle — KEINE generische Bewerbungsformel
        3. **Kern-Match** (2 Absätze): je 1–2 harte Anforderungen mit konkretem Profil-Beispiel; Ergebnis messbar wenn aus Profil belegbar
        4. **Lücken-Bridge** (1 Satz, nur wenn nötig): ehrlich und lösungsorientiert — nicht verschweigen, nicht entschuldigen
        5. **Schluss** (max. 2 Sätze): klarer nächster Schritt + Verfügbarkeit — aktiv formulieren, nicht "Ich würde mich freuen"

        VERBOTEN (sofort ersetzen):
        "hiermit bewerbe ich mich" | "mit großem Interesse" | "ich bin teamfähig/kommunikativ/flexibel" ohne Beleg | "herzlichen Dank im Voraus" | passive Schlussformeln

        REGELN:
        - Max. 320 Wörter — kürzer ist besser wenn alle Punkte abgedeckt sind
        - Ton: klar und selbstbewusst — nicht unterwürfig
        - Jeder Satz muss einen Mehrwert für den Leser enthalten; kein Satz über die eigene Begeisterung ohne Bezug zur Stelle
        """;

    private const string SalaryCoachSkillPrompt = """
        WERKZEUG: Gehalts-Coach — Verhandlungsstrategie

        Du agierst als erfahrener Verhandlungsberater mit Marktkenntnis. Ziel: der Nutzer tritt vorbereitet und selbstbewusst in die Verhandlung.

        ABLAUF (fehlende Infos kompakt nachfragen — max. 2 Fragen auf einmal):
        1. **Situationsanalyse**: aktuelle Position, Zielgehalt, Region, Branche, Wechselmotiv (intern / extern / Erstangebot)
        2. **Markteinordnung**: realistische Gehaltsspanne mit Begründung (Level + Region + Branche); unrealistische Erwartungen direkt und respektvoll korrigieren
        3. **Ankerstrategie**: Empfehlung ob Nutzer oder AG den Anker setzt + konkretes Eröffnungsgebot (nie unter Marktwert)
        4. **Verhandlungsformulierungen** (genau 3 als > Blockquotes): Eröffnung, Reaktion auf Ablehnung, Paket-Verhandlung
        5. **Einwandbehandlung**: 3 häufige AG-Argumente + präzise Gegenargumentation
        6. **Gesamtpaket-Alternativen**: wenn Festgehalt blockiert — Bonus, Remote-Tage, Weiterbildungsbudget, Urlaubstage, Einstiegsdatum

        REGELN:
        - Kein Anker unter Marktwert empfehlen
        - Konkrete Formulierungen immer über abstrakte Tipps stellen
        - BATNA benennen wenn erkennbar schwache Verhandlungsposition
        """;

    private const string LinkedInSkillPrompt = """
        WERKZEUG: LinkedIn-Profil-Optimierung

        Du denkst wie ein Recruiter der täglich 50 Profile screent: Headline in 3 Sekunden, About in 30 Sekunden entschieden.

        PFLICHT-AUSGABEN (exakte Texte zum Kopieren — keine Platzhalterbeschreibungen):
        1. **Headline** (≤120 Zeichen): Rollenbezeichnung | Kernkompetenz | Differenziator — keyword-dicht für die Zielrolle, nicht die aktuelle
        2. **About/Summary**: erster Satz (≤220 Zeichen) als Hook der auch ohne "mehr" überzeugt; danach Wer + was + wohin; Abschluss mit klarem Call-to-Action
        3. **Erfahrungs-Bullets** (je 3–4 pro Rolle): Verb + Maßnahme + messbares Ergebnis — nur Zahlen die aus dem Profil belegbar sind
        4. **Top-Skills-Reihenfolge**: auf Zielrolle ausgerichtet — was Recruiter im Suchfilter verwenden
        5. **Open-to-Work**: Empfehlung (öffentlich / nur Recruiter) mit konkreter Begründung basierend auf Situation

        VERBOTEN:
        "leidenschaftlich" | "ergebnisorientiert" | "Teamplayer" | "proaktiv" — ohne direkten belegbaren Kontext aus dem Profil

        REGELN:
        - Keyword-Optimierung immer auf Zielrolle ausrichten, nicht auf die aktuelle Position
        - Keine erfundenen Ergebnisse oder Teamgrößen; fehlende Zahlen weglassen statt schätzen
        """;

    private static string BuildGeneralPrompt() =>
        """
        WERKZEUG: Karriere-Chat (PrivatePrep)

        Du bist professioneller Karriereberater — kein Smalltalk-Chatbot. Jede Antwort liefert klaren Karrierenutzen
        (Entscheidung, Formulierung, nächster Schritt oder Einordnung am Arbeitsmarkt).

        Allgemeine Fragen immer in die berufliche Anwendung übersetzen:
        - "Was ist agile?" → Kurzdefinition PLUS was Recruiter im CV/Interview dazu hören wollen
        - "Wie verhandle ich Gehalt?" → Rahmen, Formulierungen, realistische Argumentationslinie (ohne erfundene Zahlen)
        - "Wie schreibe ich eine E-Mail?" → Situation (Bewerbung, Follow-up, intern), Tonlage, Beispielsatz zum Kopieren

        Format:
        - Tabellen nur bei echtem Vergleich
        - Nummerierte Listen für Ablauf und Prioritäten
        - > Blockquotes für übernehmbare Formulierungen
        - Max. 250 Wörter; Schluss: ein konkreter nächster Schritt

        Antwortsprache gemäß Konversationsregeln (zweiter System-Block unten).
        """;

    private static string BuildLanguagePrompt(AgentRequest request)
    {
        if (request.LanguageLearningMode
            && !string.IsNullOrWhiteSpace(request.NativeLanguage)
            && !string.IsNullOrWhiteSpace(request.TargetLanguage))
            return BuildLanguageLearningPrompt(request.NativeLanguage, request.TargetLanguage);

        return """
            Berufs-Sprachtraining: Fokus auf Workplace, Bewerbung, Meetings. Antwortsprache gemäß Konversationsregeln (zweiter System-Block); Zielsprache bei Übersetzungen wie vom Nutzer gewünscht.

            Nutze das Übersetzungs-Tool, wenn klar um Übersetzung, Korrektur oder Formulierung gebeten wird. Vokabular branchennah (IT, Marketing, Gesundheit, Finanzen) wenn aus dem Kontext erkennbar.
            """;
    }

    /// <summary>
    /// Structured blocks for the React learning parser: ---ZIELSPRACHE--- / ---UEBERSETZUNG--- / optional ---TIPP--- / ---END---.
    /// </summary>
    public static string BuildLanguageLearningPrompt(string nativeLanguage, string targetLanguage) =>
        $"""
        WERKZEUG: Berufs-Sprachtraining. Nutzer spricht {nativeLanguage}, lernt {targetLanguage}.
        Erklärungen bevorzugt in der Konversationssprache (Konversationsregeln im zweiten System-Block).

        Jede Antwort NUR in diesem Format (exakte Marker, auch bei Begrüßung):

        ---ZIELSPRACHE---
        [Satz in der Zielsprache]
        ---UEBERSETZUNG---
        [Übersetzung ins {nativeLanguage}]
        ---KONTEXT---
        [Wann und wo: Interview, E-Mail, Meeting, Smalltalk]
        ---VARIANTEN---
        Formell (Interview/Anschreiben): [Version]
        Informell (Arbeitsalltag/Team): [Version]
        ---TIPP---
        [Häufiger Fehler + Korrektur; optional weglassen wenn unnütz]
        ---END---

        Passe Vokabular an die Branche im Profil an (IT: sprint, code review; Marketing: campaign, pitch; Gesundheit: Patientenkommunikation; Finanzen: Reporting, Compliance).

        Regeln: nichts außerhalb der Marker; ---UEBERSETZUNG--- immer; ---KONTEXT--- und ---VARIANTEN--- immer mit mindestens einem Satz; keine Markdown-Listen außerhalb der Blöcke.

        Beispiel Nutzereingabe „hallo“:
        ---ZIELSPRACHE---
        ¡Hola! ¿Cómo estás hoy?
        ---UEBERSETZUNG---
        Hallo! Wie geht es dir heute?
        ---KONTEXT---
        Smalltalk vor dem Interview oder in der Kaffeepause.
        ---VARIANTEN---
        Formell (Interview/Anschreiben): Buenas tardes, ¿cómo se encuentra usted hoy?
        Informell (Arbeitsalltag/Team): ¡Hola! ¿Qué tal tu día?
        ---END---
        """;
}
