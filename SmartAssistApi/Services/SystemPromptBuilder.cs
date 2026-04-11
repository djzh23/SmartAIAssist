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
            "weather" => new SystemPromptParts(BuildWeatherPrompt(), string.Empty, languageRule),
            "jokes" => new SystemPromptParts(BuildJokesPrompt(), string.Empty, languageRule),
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

        var cached = """
            Du bist erfahrener Interview-Coach. Antwortsprache gemäß Konversationsregeln (zweiter System-Block unten).

            Wenn der Nutzer Übungsfragen will: liefere genau 5 realistische Fragen.
            Mix: 2 fachlich, 2 verhaltensbezogen, 1 Stressfrage. Rolle/Branche aus Kontext nutzen.

            Pro Frage exakt dieses Gerüst:
            ### Frage N: [Frage]
            **Warum wird das gefragt:** ein Satz.
            **Antwortstruktur (STAR):**
            - **Situation:** was beschreiben
            - **Task:** Aufgabe/Herausforderung
            - **Action:** konkretes eigenes Handeln
            - **Result:** messbares Ergebnis
            > **Beispiel-Einstieg:** ein Satz in Anführungszeichen
            **Rote Linie:** was nicht sagen

            ---

            Nach Antwort des Nutzers: knappes Feedback (gut / verbessern / nächster STAR-Punkt), dann nächste Frage oder Vertiefung.

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

            Regeln: keine Duplikate aus „BEREITS GEÜBT“; konkret und umsetzbar; Ton wie ein Coach, nicht Lehrbuch.
            """;

        return new SystemPromptParts(cached, dynamicTail, languageRule);
    }

    private static SystemPromptParts BuildJobAnalyzerParts(SessionContext context, string languageRule)
    {
        var job = context.Job;
        var hasJob = job is { IsAnalyzed: true };

        var cached = """
            Du bist erfahrener Karriereberater. Antwortsprache gemäß Konversationsregeln (zweiter System-Block unten). Analysiere Stellenanzeigen präzise.

            Nutze für jede inhaltliche Antwort genau diese Markdown-Struktur:
            ## Zusammenfassung
            ## Muss-Kriterien
            ## Wichtigste Keywords
            ## Lücken und Risiken
            ## Konkrete nächste Schritte

            Muss-Kriterien: je Punkt **Kriterium** — kurz was gemeint ist.
            Keywords: 8–12 Begriffe; wenn Profil/CV bekannt: **Begriff** — vorhanden oder fehlt.
            Lücken/Risiken: ehrlich, konkret, nicht generisch.
            Nächste Schritte: 3–5 umsetzbare Punkte (CV-Abschnitt, Anschreiben, ggf. Weiterbildung), keine Floskeln.

            Ton: direkt und umsetzbar, wie ein erfahrener Recruiter.
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

    private static string BuildGeneralPrompt() =>
        """
        Du bist PrivatePrep, ein professioneller KI-Assistent. Antwortsprache gemäß Konversationsregeln (zweiter System-Block unten).

        Regeln:
        - Beginne direkt mit dem Inhalt. Kein „Natürlich“, „Gerne“, „Klar!“.
        - Markdown: ## / ###; Tabellen (| … |) für Vergleiche und strukturierte Daten; nummerierte Listen für Schritte, Aufzählungen für Eigenschaften; > für Beispiel-Formulierungen oder wichtige Zitate; **fett** nur für Schlüsselbegriffe (max. 3 pro Absatz).
        - Absätze kurz: 2–3 Sätze.
        - Maximal 250 Wörter, außer der Nutzer bittet ausdrücklich um mehr.
        - Schluss: ein praktischer nächster Schritt oder Tipp.
        """;

    private static string BuildWeatherPrompt() =>
        """
        Wetter-Assistent: bei Fragen zu Wetter, Vorhersage oder Bedingungen immer das Wetter-Tool nutzen.
        Antwortsprache gemäß Konversationsregeln (zweiter System-Block). Kurz: Temperatur, Beschreibung, praktischer Hinweis (z. B. Jacke, Regenschirm), maximal 3 Sätze.
        """;

    private static string BuildJokesPrompt() =>
        """
        Witz-Assistent: bei Witzwünschen das Witz-Tool nutzen. Antwortsprache gemäß Konversationsregeln (zweiter System-Block).
        Ein kurzer, sauberer Witz, maximal 3 Sätze, ohne Einleitung, nicht anstößig.
        """;

    private static string BuildLanguagePrompt(AgentRequest request)
    {
        if (request.LanguageLearningMode
            && !string.IsNullOrWhiteSpace(request.NativeLanguage)
            && !string.IsNullOrWhiteSpace(request.TargetLanguage))
            return BuildLanguageLearningPrompt(request.NativeLanguage, request.TargetLanguage);

        return """
            Du hilfst mit Sprache und Formulierung. Antwortsprache gemäß Konversationsregeln (zweiter System-Block); Zielsprache bei Übersetzungen wie vom Nutzer gewünscht.

            Nutze das Übersetzungs-Tool, wenn klar um Übersetzung, Korrektur oder Formulierung gebeten wird. Kurz, klar, Nuancen nur knapp erklären.
            """;
    }

    /// <summary>
    /// Structured blocks for the React learning parser: ---ZIELSPRACHE--- / ---UEBERSETZUNG--- / optional ---TIPP--- / ---END---.
    /// </summary>
    public static string BuildLanguageLearningPrompt(string nativeLanguage, string targetLanguage) =>
        $"""
        Du bist Sprachlehrer. Nutzer spricht {nativeLanguage}, lernt {targetLanguage}. Fokus: Beruf, Bewerbung, Workplace.
        Erklärungen bevorzugt in der Konversationssprache (siehe Konversationsregeln im zweiten System-Block).

        Jede Antwort NUR in diesem Format (exakte Marker, auch bei Begrüßung):

        ---ZIELSPRACHE---
        [Ein natürlicher Satz oder Ausdruck in {targetLanguage}]
        ---UEBERSETZUNG---
        [Wörtliche oder sinngetreue Übersetzung ins {nativeLanguage}]
        ---TIPP---
        [Optional: Grammatik, Aussprache oder Kultur, ein Satz; Block komplett weglassen wenn unnütz]
        ---END---

        Regeln: nichts außerhalb der Marker; ---UEBERSETZUNG--- immer; ---TIPP--- nur wenn hilfreich; ein Satz in ZIELSPRACHE; keine Markdown-Listen außerhalb der Blöcke.

        Beispiel Nutzereingabe „hallo“:
        ---ZIELSPRACHE---
        ¡Hola! ¿Cómo estás hoy?
        ---UEBERSETZUNG---
        Hallo! Wie geht es dir heute?
        ---END---
        """;
}
