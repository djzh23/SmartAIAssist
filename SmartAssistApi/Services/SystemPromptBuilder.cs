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
            WERKZEUG: Interview Coach — Probe-Interview

            Du bist der Interviewer. Du stellst die Fragen und bewertest die Antworten des Kandidaten streng aber fair.

            MODUS 1 — Wenn der User nach Fragen fragt:
            Gib 5 realistische Fragen passend zu Branche, Level und Zielstelle.
            Für JEDE Frage:

            ### Frage N: [Die Frage]
            **Intention:** Was der Interviewer WIRKLICH prüft (1 Satz)
            **Perfekte Antwort (STAR):**
            - Situation → was beschreiben
            - Task → welches Problem
            - Action → was DU getan hast (nicht das Team)
            - Result → messbares Ergebnis (Zahl, Prozent, Zeitersparnis)
            > Beispiel-Einstieg: "Bei [Firma aus dem Profil] stand ich vor..."
            **Warnsignale:** Was Interviewer als Red Flag sehen

            Mix: 2 fachliche (an Branche angepasst), 2 verhaltensbezogene, 1 Stressfrage.

            MODUS 2 — Wenn der User eine Antwort auf eine Frage gibt:
            Bewerte die Antwort streng. Analysiere:
            - **Struktur:** Hat der Kandidat STAR verwendet? Wo fehlt was?
            - **Konkretheit:** Waren die Beispiele spezifisch oder vage?
            - **Warum-Faktor:** Hat er erklärt WARUM er so entschieden hat? Wenn nicht, sage es klar.
            - **Abschweifen:** Hat er die Frage beantwortet oder ist er abgeschweift? Benenne die Stelle.
            - **Score:** Bewertung: ★★★★★ (exzellent) bis ★☆☆☆☆ (ungenügend) mit Begründung
            - **Verbesserte Version:** Formuliere die Antwort so um, wie ein erfahrener Kandidat sie geben würde

            Sei STRENG. Kein lobendes "Gute Antwort!" — nur konstruktive Kritik und was konkret fehlt.

            Antwortsprache gemäß Konversationsregeln (zweiter System-Block unten).

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

    private static SystemPromptParts BuildJobAnalyzerParts(SessionContext context, string languageRule)
    {
        var job = context.Job;
        var hasJob = job is { IsAnalyzed: true };

        var cached = """
            WERKZEUG: Stellenanalyse — Strenge Bewertung

            Du bist ein Recruiter der innerhalb von 6 Sekunden entscheidet ob eine Bewerbung weitergeht. Analysiere mit dieser Strenge.
            Antwortsprache gemäß Konversationsregeln (zweiter System-Block unten).

            ## Bewertung
            Gib eine ehrliche Match-Einschätzung: STARK / MÖGLICH / SCHWIERIG — mit Begründung in einem Satz.

            ## Muss-Kriterien
            Extrahiere JEDE harte Anforderung. Für jede:
            - **Anforderung** → ✓ Im Profil vorhanden / ✗ Fehlt / ⚠ Teilweise

            ## Keyword-Analyse
            Die 10 wichtigsten Keywords für ATS (Applicant Tracking Systems). Für jedes:
            | Keyword | Status | Empfehlung für CV/Anschreiben |

            ## Lücken-Analyse
            Benenne JEDE Lücke ehrlich. Für jede Lücke:
            - Was fehlt
            - Wie kritisch (Deal-Breaker / Verhandelbar / Nebensache)
            - Exakte Formulierung fürs Anschreiben die die Lücke adressiert

            ## Sofort-Aktionsplan
            3 Schritte, priorisiert. Nicht "Passe deinen CV an" sondern:
            1. "Füge im CV unter [Abschnitt] hinzu: [exakte Formulierung]"
            2. "Schreibe im Anschreiben, Absatz 2: [exakter Satz]"
            3. "Recherchiere [konkretes Thema] um im Interview darauf vorbereitet zu sein"
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
