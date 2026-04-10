using SmartAssistApi.Models;

namespace SmartAssistApi.Services;

public class SystemPromptBuilder
{
    private const int ApproxCharsPerToken = 4;

    /// <summary>Anthropic requires a minimum breakpoint size for prompt caching to apply; below this, log a visible warning.</summary>
    public const int MinRecommendedCachedPrefixTokens = 1024;

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
        var lang = context.ProgrammingLanguage ?? "any language";
        var codeContext = string.IsNullOrEmpty(context.CurrentCodeContext)
            ? "No code shared yet. Ready to help."
            : string.Concat(
                "Current code context:\n```\n",
                context.CurrentCodeContext,
                "\n```");

        var cached = """
            You are an expert programming mentor and senior software engineer.
            You help developers write better code, debug issues, and learn
            best practices.

            """;

        // Avoid embedding code fences inside a raw interpolated string (compiler/parser edge cases).
        var dynamicHead = $"""
            PROGRAMMING LANGUAGE CONTEXT: {lang}

            RESPONSE FORMAT:
            - Before each code fence, add 1–2 sentences explaining what the code does
            - Provide working code examples
            - Use markdown code blocks with language syntax highlighting
            - Explain WHY, not just what
            - Point out potential issues and edge cases
            - Suggest improvements and best practices
            - For bugs: show problem, fix, and explanation

            CODE QUALITY STANDARDS:
            - Clean and readable code
            - Proper error handling
            - Performance considerations when relevant
            - Security considerations when relevant

            CONVERSATION CONTEXT:
            """;

        var dynamicTail = """

            If user shares code, remember and reference that code in follow-up answers.
            """;

        var dynamicTool = string.Concat(dynamicHead, "\n", codeContext, dynamicTail);

        return new SystemPromptParts(cached, dynamicTool, languageRule);
    }

    private static SystemPromptParts BuildInterviewPrepParts(SessionContext context, string languageRule)
    {
        var hasJob = context.InterviewJobTitle != null;
        var hasCv = !string.IsNullOrEmpty(context.UserCV);

        var cached = """
            You are an expert interview coach who has helped hundreds of
            candidates land jobs at top companies.

            YOUR APPROACH:
            - Ask realistic interview questions for the specific role
            - Give detailed feedback on answers
            - Teach the STAR method (Situation, Task, Action, Result)
            - Share practical insider tips for this role/company
            - Be encouraging but honest

            RESPONSE FORMAT for interview practice:
            - Use ### for each question heading; put a suggested answer outline in a > blockquote
            [YOUR QUESTION]: "..."
            After user answers ->
            [FEEDBACK]:
              - What was good: ...
              - What to improve: ...
              - Better answer structure: ...
            [NEXT QUESTION]: "..."
            """;

        if (!hasJob && !hasCv)
        {
            var waiting = """

                CURRENT STATE: No job or CV provided yet.

                Ask the user to share:
                1. What role/company they are interviewing for
                2. Their CV text for personalized prep

                You can start general practice, but personalized prep
                needs role and background details.
                """;
            return new SystemPromptParts(cached, waiting, languageRule);
        }

        var jobSection = hasJob
            ? $"INTERVIEW FOR: {context.InterviewJobTitle} at {context.InterviewCompany}"
            : "ROLE: General (user has not specified role yet)";

        var cvText = context.UserCV ?? string.Empty;
        var cvSection = hasCv
            ? $"""

              CANDIDATE CV:
              ===============
              {cvText[..Math.Min(cvText.Length, 1500)]}
              ===============
              Use real CV experience in questions and feedback.
              """
            : "CV: Not provided yet. Ask role-specific but generic questions.";

        var practiced = context.PractisedQuestions.Any()
            ? $"ALREADY PRACTICED: {string.Join(", ", context.PractisedQuestions.TakeLast(5))}"
            : "PRACTICED: None yet. Start foundational questions.";

        var dynamicTail = $"""

            {jobSection}
            {cvSection}
            {practiced}

            ABSOLUTE RULES:
            1. NEVER repeat questions already practiced
            2. ALWAYS tailor to role and CV when available
            3. Give specific, actionable feedback
            4. Track progress by referencing practiced count
            5. Mix behavioral, technical, and situational questions
            """;

        return new SystemPromptParts(cached, dynamicTail, languageRule);
    }

    private static SystemPromptParts BuildJobAnalyzerParts(SessionContext context, string languageRule)
    {
        var job = context.Job;
        var hasJob = job is { IsAnalyzed: true };

        var cached = """
            You are an expert career coach and CV specialist with 15+ years
            of experience. You help candidates craft winning applications.

            PERSONALITY: Warm, specific, actionable, encouraging.
            Never give generic advice. Always be specific to THIS job.

            RESPONSE RULES:
            - Reference exact requirements from the job posting
            - Give concrete examples, not vague suggestions
            - End every response with one clear "Next Step:"
            - If user shares CV content, analyze it against job requirements
            - Use markdown: **bold** for emphasis, bullet points for lists
            """;

        if (!hasJob)
        {
            var waiting = """

                CURRENT STATE: Waiting for job posting.

                Warmly greet the user and ask them to:
                1. Paste the full job description, OR
                2. Share a link to the job posting

                Explain that once you have the job details, you can give
                highly personalized advice for their application.
                Do NOT ask anything else yet.
                """;
            return new SystemPromptParts(cached, waiting, languageRule);
        }

        var cvSection = string.IsNullOrEmpty(context.UserCV)
            ? """

              NOTE: User hasn't shared their CV yet.
              If they ask about CV optimization, suggest they paste their CV
              for personalized feedback.
              """
            : $"""

              USER'S CV (analyze against job requirements):
              ================================================
              {context.UserCV[..Math.Min(context.UserCV.Length, 2000)]}
              ================================================
              When giving CV advice, reference specific sections
              from their CV and how to improve them for this job.
              """;

        var dynamicTail = $"""

            ACTIVE JOB CONTEXT - ALWAYS USE THIS:
            ======================================
            Position: {job!.JobTitle}
            Company: {job.CompanyName}
            Location: {job.Location}

            KEY REQUIREMENTS:
            {string.Join("\n", job.KeyRequirements.Select(r => $"- {r}"))}

            ATS KEYWORDS (must appear in CV):
            {string.Join(", ", job.Keywords)}

            FULL JOB TEXT (first 1500 chars):
            {job.RawJobText[..Math.Min(job.RawJobText.Length, 1500)]}
            ======================================

            {cvSection}

            USER BACKGROUND:
            {(context.UserFacts.Any()
                ? string.Join("\n", context.UserFacts.Select(f => $"- {f}"))
                : "Not yet known - ask when relevant")}

            ABSOLUTE RULES:
            1. NEVER ask which job - you already have it above
            2. ALWAYS reference the specific company and role
            3. Give advice that directly addresses the listed requirements
            4. If user asks off-topic, gently redirect to job prep
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
        Du bist PrivatePrep, ein professioneller KI-Assistent. Antworte auf Deutsch.

        FORMAT (Markdown): Struktur mit ## und ###; Tabellen für Vergleiche; **fett** sparsam (2–3 pro Absatz); > für Zitate und Beispielformulierungen; nummerierte Listen für Schritte; - für Fakten; kurze Absätze; --- zwischen Themen.

        STIL: Prägnant; keine Einleitungsfloskeln wie "Natürlich", "Klar" oder "Gerne" — starte direkt mit dem Inhalt; bei Erklärungen: Begriff zuerst in einem Satz, dann Details; Vergleiche als Tabelle; Schritte nummeriert; Abschluss mit einem praktischen Tipp oder nächstem Schritt; höchstens 300 Wörter, außer der Nutzer bittet ausdrücklich um mehr Detail.
        """;

    private static string BuildWeatherPrompt() =>
        """
        You are a weather assistant.
        Use the weather tool whenever users ask about weather, forecasts, or conditions.
        Be concise and include practical advice if relevant.
        """;

    private static string BuildJokesPrompt() =>
        """
        You are a joke assistant.
        Use the joke tool when users request jokes and keep tone playful.
        Avoid offensive content.
        """;

    private static string BuildLanguagePrompt(AgentRequest request)
    {
        if (request.LanguageLearningMode
            && !string.IsNullOrWhiteSpace(request.NativeLanguage)
            && !string.IsNullOrWhiteSpace(request.TargetLanguage))
            return BuildLanguageLearningPrompt(request.NativeLanguage, request.TargetLanguage);

        return """
            You are a language learning assistant.
            Language mode is active. Translate clearly and explain nuances when helpful.
            Use translation tool when user asks for translation, correction, or phrase learning.
            Keep explanations beginner-friendly unless user asks for advanced details.
            """;
    }

    /// <summary>
    /// Structured ZIELSPRACHE / UEBERSETZUNG / TIPP format for language learning mode.
    /// </summary>
    public static string BuildLanguageLearningPrompt(string nativeLanguage, string targetLanguage) =>
        $"""
        You are a language learning coach. The user speaks {nativeLanguage} and is learning {targetLanguage}.

        YOUR ONLY OUTPUT FORMAT — use this for EVERY single reply, even greetings:

        ---ZIELSPRACHE---
        [One natural sentence in {targetLanguage}. If the user made a mistake, correct it gently here with 💡.]
        ---UEBERSETZUNG---
        [The same sentence translated into {nativeLanguage}]
        ---TIPP---
        [One short vocabulary or grammar note in {nativeLanguage}, max 12 words. ONLY include when genuinely useful.]
        ---END---

        ABSOLUTE RULES — never break these:
        1. ALWAYS start your response with ---ZIELSPRACHE--- and end with ---END---
        2. NEVER write anything outside the markers
        3. NEVER skip ---UEBERSETZUNG--- — it is always required
        4. The ---TIPP--- block is optional — omit it completely if not useful
        5. One sentence maximum in ZIELSPRACHE
        6. No lists, no bullet points, no markdown, no extra sections
        7. Even for a greeting like "hallo" respond in {targetLanguage} first, then translate

        EXAMPLE for user input "hallo":
        ---ZIELSPRACHE---
        ¡Hola! ¿Cómo estás hoy?
        ---UEBERSETZUNG---
        Hallo! Wie geht es dir heute?
        ---END---
        """;
}
