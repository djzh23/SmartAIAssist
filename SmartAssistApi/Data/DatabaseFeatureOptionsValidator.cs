using Microsoft.Extensions.Options;

namespace SmartAssistApi.Data;

public sealed class DatabaseFeatureOptionsValidator : IValidateOptions<DatabaseFeatureOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseFeatureOptions options)
    {
        if (!IsRedisOrPostgres(options.ChatNotesStorage))
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:ChatNotesStorage must be \"redis\" or \"postgres\".");
        }

        if (!IsRedisOrPostgres(options.JobApplicationsStorage))
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:JobApplicationsStorage must be \"redis\" or \"postgres\".");
        }

        if (!IsRedisOrPostgres(options.CareerProfileStorage))
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:CareerProfileStorage must be \"redis\" or \"postgres\".");
        }

        if (string.Equals(options.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase)
            && !options.PostgresEnabled)
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:ChatNotesStorage=postgres requires DatabaseFeatures:PostgresEnabled=true.");
        }

        if (string.Equals(options.JobApplicationsStorage, "postgres", StringComparison.OrdinalIgnoreCase)
            && !options.PostgresEnabled)
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:JobApplicationsStorage=postgres requires DatabaseFeatures:PostgresEnabled=true.");
        }

        if (string.Equals(options.CareerProfileStorage, "postgres", StringComparison.OrdinalIgnoreCase)
            && !options.PostgresEnabled)
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:CareerProfileStorage=postgres requires DatabaseFeatures:PostgresEnabled=true.");
        }

        // Do not require ConnectionStrings:Supabase here: it may be supplied only via DATABASE_URL /
        // SUPABASE__CONNECTIONSTRING after normalization, or missing until secrets are fixed.
        // ChatNotesService uses Redis when ChatNotesPostgresService is not registered.
        return ValidateOptionsResult.Success;
    }

    private static bool IsRedisOrPostgres(string value) =>
        string.Equals(value, "redis", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "postgres", StringComparison.OrdinalIgnoreCase);
}
