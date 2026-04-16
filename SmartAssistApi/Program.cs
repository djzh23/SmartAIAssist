using SmartAssistApi.Services;
using SmartAssistApi.Services.Groq;

var builder = WebApplication.CreateBuilder(args);
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
var allowedOrigins = localOrigins
    .Concat(configuredOrigins)
    .Concat(envOrigins)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.Configure<GroqOptions>(builder.Configuration.GetSection(GroqOptions.SectionName));
builder.Services.AddHttpClient<GroqChatCompletionService>(client =>
{
    client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
    client.Timeout = TimeSpan.FromMinutes(2);
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
builder.Services.AddScoped<ChatNotesService>();
builder.Services.AddScoped<ApplicationService>();
builder.Services.AddScoped<ClerkAuthService>();
builder.Services.AddScoped<IStripeApiClient, StripeApiClient>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddHostedService<ConversationCleanupService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorClient", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("X-Usage-Today", "X-Usage-Limit", "X-Usage-Plan");
    });
});

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
startupLogger.LogInformation("CORS allowed origins: {Origins}", string.Join(", ", allowedOrigins));
var azureSpeechKey = app.Configuration["AZURE_SPEECH_KEY"] ?? Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
if (string.IsNullOrWhiteSpace(azureSpeechKey))
{
    startupLogger.LogWarning(
        "Azure Speech API key is not configured. TTS will fail. Set AZURE_SPEECH_KEY as an environment variable.");
}

app.UseCors("BlazorClient");

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/stripe/webhook"))
        context.Request.EnableBuffering();

    await next();
});

app.MapControllers();
app.Run();
