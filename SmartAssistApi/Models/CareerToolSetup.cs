namespace SmartAssistApi.Models;

/// <summary>Optional strukturierter Kontext für Job-Analyzer und Interview-Coach (vom Frontend gesendet).</summary>
public record CareerToolSetup(
    string? CvText = null,
    string? JobText = null,
    string? JobUrl = null,
    string? JobTitle = null,
    string? CompanyName = null,
    string? InterviewLanguageCode = null,
    bool JobAnalyzerFollowUp = false,
    string? InterviewAlias = null);
