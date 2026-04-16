using Microsoft.Extensions.Options;

namespace SmartAssistApi.Data;

public sealed class DatabaseFeatureOptionsValidator : IValidateOptions<DatabaseFeatureOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseFeatureOptions options)
    {
        if (!string.Equals(options.ChatNotesStorage, "redis", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:ChatNotesStorage must be \"redis\" or \"postgres\".");
        }

        if (string.Equals(options.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase)
            && !options.PostgresEnabled)
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:ChatNotesStorage=postgres requires DatabaseFeatures:PostgresEnabled=true.");
        }

        // Do not require ConnectionStrings:Supabase here: it may be supplied only via DATABASE_URL /
        // SUPABASE__CONNECTIONSTRING after normalization, or missing until secrets are fixed.
        // ChatNotesService uses Redis when ChatNotesPostgresService is not registered.
        return ValidateOptionsResult.Success;
    }
}
