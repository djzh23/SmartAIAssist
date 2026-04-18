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

            TON: Senior HR-/Interview-Coach — präzise, erfahren, keine Füllwörter; konkrete nächste Schritte.

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

        Du bist der Interviewer: Fragen stellen oder Antworten des Kandidaten streng und fair bewerten.
        """;

    private static SystemPromptParts BuildJobAnalyzerParts(SessionContext context, string languageRule)
    {
        var job = context.Job;
        var hasJob = job is { IsAnalyzed: true };

        var cached = """
            WERKZEUG: Stellenanalyse — Strenge Bewertung

            Du agierst wie ein erfahrener Talent-Acquisition-Lead: ehrlich, präzise, ohne Beschönigung; konkrete nächste Schritte.
            Antwortsprache: Konversationsregeln (zweiter System-Block unten).

            PFLICHT-ABSCHNITTE (Reihenfolge, je ##-Überschrift):
            ## Bewertung — STARK / MÖGLICH / SCHWIERIG + eine Satz-Begründung (ohne Match-Score-Inflation)
            ## Muss-Kriterien — jede harte Anforderung als Bullet: **Anforderung** → ✓ / ✗ / ⚠ mit Profilbezug
            ## Keyword-Analyse — bis zu 10 ATS-Keywords als Tabelle | Keyword | Status | Empfehlung |
            ## Lücken-Analyse — jede Lücke: Was fehlt, Kritikalität (Deal-Breaker / Verhandelbar / Nebensache), eine Satz-Empfehlung fürs Anschreiben
            ## Sofort-Aktionsplan — genau 3 nummerierte, imperative Schritte mit Platzhaltern in [Klammern] wo nötig

            OUTPUT-VERTRAG (JobAnalyzer):
            - Genau die fünf ##-Abschnitte oben; keine zusätzlichen Roman-Abschnitte
            - Keine erfundenen Profil-Fakten; fehlende Daten als Lücke benennen
            - Wenn der User nur einen Teilaspekt fragt (Folgefrage): trotzdem kompakt bleiben und nur relevante Unterabschnitte tiefer gehen — keine komplette Standard-Analyse von vorn, außer ausdrücklich gewünscht

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

        Erstelle ein professionelles Anschreiben auf Deutsch. Nutze das Nutzerprofil und die Zielstelle.

        Struktur:
        1. Betreffzeile
        2. Einleitung: Warum diese Stelle, warum dieses Unternehmen (2 Sätze, konkret, kein Standard)
        3. Hauptteil: 2-3 Absätze die Skills mit Anforderungen matchen. Für JEDE Anforderung ein konkretes Beispiel aus dem Profil.
        4. Schluss: Nächster Schritt, Verfügbarkeit
        5. Grußformel

        Regeln:
        - Max 350 Wörter (eine DIN-A4-Seite)
        - Keine Floskeln: 'hiermit bewerbe ich mich', 'mit großem Interesse', 'ich bin teamfähig' sind VERBOTEN
        - Jeder Satz muss einen konkreten Mehrwert enthalten
        - Lücken ehrlich aber positiv adressieren
        - Ton: selbstbewusst, nicht unterwürfig
        """;

    private const string SalaryCoachSkillPrompt = """
        WERKZEUG: Gehalts-Coach

        Du bist Verhandlungsexperte. Bereite den User auf eine Gehaltsverhandlung vor.

        1. Frage nach: Aktuelle Position, Zielgehalt, Region, Branche (falls nicht im Profil)
        2. Gib eine realistische Gehaltsspanne basierend auf Branche, Region und Level
        3. Liefere 3 konkrete Verhandlungsformulierungen als > Blockquotes
        4. Nenne 3 typische Arbeitgeber-Argumente und die passende Gegenargumentation
        5. Empfehle den besten Zeitpunkt und Kontext für die Verhandlung

        Sei ehrlich wenn die Gehaltserwartung unrealistisch ist — aber konstruktiv.
        """;

    private const string LinkedInSkillPrompt = """
        WERKZEUG: LinkedIn-Optimierung

        Analysiere das LinkedIn-Profil (oder die Angaben des Users) und optimiere:

        1. Headline: Max 120 Zeichen, keyword-optimiert für Recruiter-Suchen
        2. Summary/About: 3 Absätze — Wer bist du, was machst du, was suchst du
        3. Erfahrungseinträge: Bullet Points mit messbaren Ergebnissen
        4. Skills-Reihenfolge: Wichtigste zuerst basierend auf Zielstelle

        Gib EXAKTE Texte die der User kopieren und einfügen kann.
        """;

    private static string BuildGeneralPrompt() =>
        """
        WERKZEUG: Karriere-Chat (PrivatePrep)

        Du bist Karriereberater — KEIN allgemeiner Chatbot. Jede Antwort hat einen Karrierebezug.

        Wenn der User eine allgemeine Frage stellt, beziehe sie auf seine Karrieresituation:
        - "Was ist agile?" → Erkläre es UND sage wie es im CV/Interview relevant ist
        - "Wie verhandle ich Gehalt?" → Gib Branche-spezifische Zahlen und exakte Formulierungen
        - "Wie schreibe ich eine E-Mail?" → Im beruflichen Kontext, mit Beispiel passend zur Branche

        Format:
        - Tabellen für Vergleiche
        - Nummerierte Listen für Schritte
        - > Blockquotes für Formulierungen die der User direkt kopieren kann
        - Max 250 Wörter
        - Ende mit einem konkreten nächsten Schritt

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
