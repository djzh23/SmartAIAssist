using System.Text.RegularExpressions;

namespace SmartAssistApi.Services;

/// <summary>
/// Normalizes inbound user text before LLM calls. Does not run on server-side session context
/// (job/CV blobs injected from <see cref="SessionContext"/>).
/// </summary>
public static class UserInputCleaner
{
    public static string CleanUserInput(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input ?? string.Empty;

        var s = input.Trim();
        s = Regex.Replace(s, "<[^>]+>", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"\n{3,}", "\n\n");
        s = Regex.Replace(s, @" {2,}", " ");
        s = s.Trim();

        const int maxLen = 4000;
        if (s.Length > maxLen)
            s = s[..maxLen] + "\n[Text gekürzt]";

        return s;
    }
}
