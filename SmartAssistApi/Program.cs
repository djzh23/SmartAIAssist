using Microsoft.EntityFrameworkCore;
using Npgsql;
using Serilog;
using SmartAssistApi.Configuration;
using SmartAssistApi.Data;
using SmartAssistApi.Services;
using SmartAssistApi.Services.Groq;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});
builder.Configuration.AddUserSecrets<Program>(optional: true);

// Map alternative env var names used on Render to the config keys the app expects.
// Render uses UPSTASH_REDIS_REST_URL / UPSTASH_REDIS_REST_TOKEN while docker-compose
// maps to UPSTASH__RESTURL / UPSTASH__RESTTOKEN via its env block.
var upstashUrl = Environment.GetEnvironmentVariable("UPSTASH_REDIS_REST_URL")
    ?? Environment.GetEnvironmentVariable("UPSTASH__RESTURL");
var upstashToken = Environment.GetEnvironmentVariable("UPSTASH_REDIS_REST_TOKEN")
    ?? Environment.GetEnvironmentVariable("UPSTASH__RESTTOKEN");
if (upstashUrl   is not null) builder.Configuration["Upstash:RestUrl"]   = upstashUrl;
if (upstashToken is not null) builder.Configuration["Upstash:RestToken"] = upstashToken;

// Hosting env (e.g. Render): FRONTEND__BASEURL must override appsettings.json's localhost default;
// do not use "config ?? env" or a non-empty localhost from JSON blocks FRONTEND__BASEURL entirely.
var frontendFromEnv = Environment.GetEnvironmentVariable("FRONTEND__BASEURL")?.Trim();
if (!string.IsNullOrWhiteSpace(frontendFromEnv))
    builder.Configuration["Frontend:BaseUrl"] = frontendFromEnv;

var groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
if (!string.IsNullOrWhiteSpace(groqKey)) builder.Configuration["Groq:ApiKey"] = groqKey;
var groqModel = Environment.GetEnvironmentVariable("GROQ_MODEL");
if (!string.IsNullOrWhiteSpace(groqModel)) builder.Configuration["Groq:Model"] = groqModel;

var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort))
{
    builder.WebHost.UseUrls($"http://*:{renderPort}");
}

var localOrigins = new[]
{
    "http://localhost:5194",
    "http://localhost:5108",
    "http://localhost:5000",
    "http://localhost:7000",
    "https://localhost:7001",
    "http://localhost:5173",
    "http://localhost:5174",
    "http://localhost:5175",
    "http://localhost:5176",
};
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var envOrigins = (Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
// Production: allow the same origin as FRONTEND__BASEURL / Frontend:BaseUrl (env injected above when set).
var frontendBaseUrl = builder.Configuration["Frontend:BaseUrl"]?.Trim();
var frontendOrigin = Array.Empty<string>();
if (!string.IsNullOrWhiteSpace(frontendBaseUrl))
{
    try
    {
        var u = new Uri(frontendBaseUrl, UriKind.Absolute);
        frontendOrigin = [u.GetLeftPart(UriPartial.Authority)];
    }
    catch
    {
        // invalid URL — skip; startup log still lists explicit CORS origins
    }
}

var allowedOrigins = localOrigins
    .Concat(configuredOrigins)
    .Concat(envOrigins)
    .Concat(frontendOrigin)
    .Where(static o => !string.IsNullOrWhiteSpace(o))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddControllers();

builder.Services.Configure<DatabaseFeatureOptions>(builder.Configuration.GetSection(DatabaseFeatureOptions.SectionName));
builder.Services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<DatabaseFeatureOptions>, DatabaseFeatureOptionsValidator>();

// Last-wins in-memory value so a valid URI from DATABASE_URL / SUPABASE__… overrides empty JSON / bad placeholders.
var supabaseResolution = SupabaseConnectionString.Resolve(builder.Configuration);
var supabaseConnectionString = supabaseResolution.ConnectionString;
if (supabaseConnectionString is not null)
    builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>("ConnectionStrings:Supabase", supabaseConnectionString)]);

var registerPostgres = !string.IsNullOrWhiteSpace(supabaseConnectionString);
if (registerPostgres)
{
    builder.Services.AddDbContext<SmartAssistDbContext>(options =>
        options.UseNpgsql(supabaseConnectionString));
    builder.Services.AddScoped<ChatNotesPostgresService>();
}

var databaseFeaturesPreview = builder.Configuration.GetSection(DatabaseFeatureOptions.SectionName)
    .Get<DatabaseFeatureOptions>() ?? new DatabaseFeatureOptions();
var registerPostgresHealth = registerPostgres && databaseFeaturesPreview.PostgresEnabled;
builder.Services.AddSmartAssistHealthChecks(registerPostgresCheck: registerPostgresHealth);
builder.Services.AddSmartAssistRateLimiter();
builder.Services.AddMemoryCache();
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection(GroqOptions.SectionName));
builder.Services.AddHttpClient<GroqChatCompletionService>(client =>
{
    client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    // LLM calls: avoid holding sockets for two minutes on stalled responses (was 120s).
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton<ConversationService>();
builder.Services.AddSingleton<SystemPromptBuilder>();
builder.Services.AddScoped<PromptComposer>();
builder.Services.AddScoped<JobContextExtractor>();
builder.Services.AddScoped<CvParsingService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<IAgentService>(sp => sp.GetRequiredService<AgentService>());
builder.Services.AddScoped<ILlmSingleCompletionService, AgentLlmSingleCompletionService>();
builder.Services.AddHttpClient<ISpeechService, AzureSpeechService>();
builder.Services.AddHttpClient<UsageService>();
builder.Services.AddHttpClient<CareerProfileService>();
builder.Services.AddHttpClient<LearningMemoryService>();
builder.Services.AddHttpClient<TokenTrackingService>();
builder.Services.AddHttpClient<UpstashRedisStringStore>();
builder.Services.AddScoped<IRedisStringStore>(sp => sp.GetRequiredService<UpstashRedisStringStore>());
builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<ChatNotesRedisService>();
builder.Services.AddScoped<ChatNotesService>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<ClerkAuthService>();
builder.Services.AddScoped<IStripeApiClient, StripeApiClient>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddHostedService<ConversationCleanupService>();
builder.Services.AddCors(options =>
{
    // Primary production UI: React (SmartAssist-react). Blazor WASM client is optional / legacy in repo.
    options.AddPolicy("SmartAssistWeb", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .WithHeaders(
                "Authorization",
                "Content-Type",
                "Accept",
                "Stripe-Signature",
                "X-Requested-With",
                "X-Request-Id")
            .WithExposedHeaders(
                "X-Usage-Today",
                "X-Usage-Limit",
                "X-Usage-Plan",
                "X-Request-Id",
                "X-Chat-Notes-Effective-Storage",
                "X-Chat-Notes-Configured-Storage",
                "X-Chat-Notes-Degraded",
                "X-Chat-Notes-Degraded-Reason");
    });
});

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("CORS allowed origins: {Origins}", string.Join(", ", allowedOrigins));
if (supabaseConnectionString is not null)
{
    try
    {
        var parsed = new NpgsqlConnectionStringBuilder(supabaseConnectionString);
        startupLogger.LogInformation(
            "Supabase: Npgsql connection resolved from {SourceKey}. Host={Host}; Database={Database}; SSL={SslMode}. (password not logged)",
            supabaseResolution.SourceKey ?? "unknown",
            parsed.Host,
            parsed.Database,
            parsed.SslMode);
    }
    catch
    {
        startupLogger.LogInformation(
            "Supabase: Npgsql connection resolved from {SourceKey}. (password not logged)",
            supabaseResolution.SourceKey ?? "unknown");
    }
}
else
{
    startupLogger.LogWarning(
        "Supabase: no EF connection registered. SourceKey={SourceKey}; Reason={Reason}. "
        + "Check Render env DATABASE_URL or SUPABASE__CONNECTIONSTRING (two underscores) or ConnectionStrings__Supabase.",
        supabaseResolution.SourceKey ?? "(none)",
        supabaseResolution.RejectReason ?? "unknown");
}

if (databaseFeaturesPreview.PostgresEnabled
    && string.Equals(databaseFeaturesPreview.ChatNotesStorage, "postgres", StringComparison.OrdinalIgnoreCase)
    && string.IsNullOrWhiteSpace(supabaseConnectionString))
{
    startupLogger.LogWarning(
        "DatabaseFeatures: ChatNotesStorage=postgres but no valid Supabase connection was resolved "
        + "(set ConnectionStrings:Supabase, DATABASE_URL, or SUPABASE__CONNECTIONSTRING). "
        + "Chat notes will use Redis until a valid Postgres connection is available.");
}
var azureSpeechKey = app.Configuration["AZURE_SPEECH_KEY"] ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
if (string.IsNullOrWhiteSpace(azureSpeechKey))
{
    startupLogger.LogWarning(
        "Azure Speech API key is not configured. TTS will fail. Set AZURE_SPEECH_KEY as an environment variable.");
}

app.UseRouting();
app.UseCors("SmartAssistWeb");

app.UseRequestId();
app.UseSerilogRequestLogging();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/stripe/webhook"))
        context.Request.EnableBuffering();

    await next();
});

app.UseRateLimiter();
app.UseSmartAssistApiSecurityHeaders();

app.MapHealthChecks("/api/health");
// Endpoint routing: attach named CORS policy to API controllers (fixes missing ACAO on some hosts).
app.MapControllers().RequireCors("SmartAssistWeb");

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
