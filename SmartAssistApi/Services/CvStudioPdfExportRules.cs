namespace SmartAssistApi.Services;

/// <summary>CV.Studio PDF export limits by subscription plan (aligned with Stripe plan slugs).</summary>
public static class CvStudioPdfExportRules
{
    /// <summary>Maximum stored PDF export rows per user. Deleting a row frees a slot.</summary>
    public static int LimitForPlan(string? plan)
    {
        return plan?.Trim().ToLowerInvariant() switch
        {
            "pro" => 10,
            "premium" => 10,
            _ => 3, // free / unknown → Starter-equivalent
        };
    }
}
