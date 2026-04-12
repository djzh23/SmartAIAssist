using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SmartAssistApi.Models;
using UglyToad.PdfPig;

namespace SmartAssistApi.Services;

/// <summary>
/// PDF-Rohtext-Extraktion und KI-gestütztes Mapping auf Profil-DTOs.
/// </summary>
public class CvParsingService
{
    /// <summary>
    /// Extrahiert Rohtext aus einer PDF-Datei (Base64-encoded, optional data-URL-Präfix).
    /// </summary>
    public string ExtractTextFromPdf(string base64Pdf)
    {
        var raw = StripDataUrlPrefix(base64Pdf.Trim());
        var pdfBytes = Convert.FromBase64String(raw);

        using var document = PdfDocument.Open(pdfBytes);

        var textParts = new List<string>();
        foreach (var page in document.GetPages())
            textParts.Add(page.Text);

        var fullText = string.Join("\n", textParts);

        fullText = Regex.Replace(fullText, @"\s{3,}", "\n");
        fullText = fullText.Trim();

        if (fullText.Length > 3000)
            fullText = fullText[..3000];

        return fullText;
    }

    /// <summary>
    /// Nutzt die KI, um aus dem CV-Rohtext strukturierte Profil-Felder zu extrahieren.
    /// </summary>
    public async Task<ParsedCvData> ParseCvWithAi(string cvText, Func<string, Task<string>> llmCall)
    {
        var prompt = """
            Extrahiere aus folgendem Lebenslauf die strukturierten Daten.
            Antworte NUR mit JSON, kein anderer Text. Format:

            {
              "currentRole": "aktuelle oder letzte Berufsbezeichnung",
              "field": "it|marketing|finance|healthcare|engineering|education|sales|hr|legal|trades|design|other",
              "level": "entry|junior|mid|senior|lead",
              "skills": ["Skill1", "Skill2"],
              "experience": [{"title": "Titel", "company": "Firma", "duration": "2022-2024"}],
              "education": [{"degree": "Abschluss", "institution": "Uni/Schule", "year": "2024"}],
              "languages": [{"name": "Englisch", "level": "B2"}]
            }

            Wenn ein Feld nicht im CV vorkommt, lasse es leer oder als leeres Array.
            Extrahiere maximal 15 Skills, 5 Erfahrungen, 3 Ausbildungen, 5 Sprachen.

            LEBENSLAUF:
            """ + cvText;

        var response = await llmCall(prompt).ConfigureAwait(false);

        try
        {
            response = response.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```", "")
                .Trim();
            return JsonSerializer.Deserialize<ParsedCvData>(response, ParsedCvJson.Options)
                   ?? new ParsedCvData();
        }
        catch
        {
            return new ParsedCvData();
        }
    }

    private static string StripDataUrlPrefix(string input)
    {
        var idx = input.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? input[(idx + "base64,".Length)..] : input;
    }

    private static class ParsedCvJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}

public sealed class ParsedCvData
{
    public string? CurrentRole { get; set; }
    public string? Field { get; set; }
    public string? Level { get; set; }
    public List<string> Skills { get; set; } = new();
    public List<WorkExperience> Experience { get; set; } = new();
    public List<Education> Education { get; set; } = new();
    public List<ProfileLanguageEntry> Languages { get; set; } = new();
}
