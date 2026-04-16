using Microsoft.Extensions.Options;

namespace SmartAssistApi.Data;

public sealed class DatabaseFeatureOptionsValidator(IConfiguration configuration) : IValidateOptions<DatabaseFeatureOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseFeatureOptions options)
    {
        if (!string.Equals(options.ChatNotesStorage, "redis", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "DatabaseFeatures:ChatNotesStorage must be \"redis\" or \"postgres\".");
        }

        if (string.Equals(options.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (!options.PostgresEnabled)
                return ValidateOptionsResult.Fail(
                    "DatabaseFeatures:ChatNotesStorage=postgres requires DatabaseFeatures:PostgresEnabled=true.");

            var conn = configuration.GetConnectionString("Supabase");
            if (string.IsNullOrWhiteSpace(conn))
            {
                return ValidateOptionsResult.Fail(
                    "DatabaseFeatures:ChatNotesStorage=postgres requires ConnectionStrings:Supabase.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
