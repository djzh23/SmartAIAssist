namespace SmartAssistApi.Services;

/// <summary>Shared limits for Redis and Postgres career profile storage (aligned with PDF extraction / token caps).</summary>
public static class CareerProfileStorageLimits
{
    public const int CvRawTextInProfileMax = 3000;
    public const int CvRawSeparateKeyMax = 50_000;
    public const int TargetJobDescriptionMax = 2000;
}
