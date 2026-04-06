using SmartAssistApi.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);

var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(renderPort))
{
    builder.WebHost.UseUrls($"http://*:{renderPort}");
}

var localOrigins = new[]
{
    "http://localhost:5194",
    "http://localhost:5000",
    "http://localhost:7000",
    "https://localhost:7001"
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
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddHttpClient<ISpeechService, ElevenLabsSpeechService>();
builder.Services.AddHttpClient<UsageService>();
builder.Services.AddScoped<ClerkAuthService>();
builder.Services.AddScoped<IStripeApiClient, StripeApiClient>();
builder.Services.AddScoped<StripeService>();
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
var elevenLabsApiKey = app.Configuration["ELEVENLABS_API_KEY"] ?? app.Configuration["ElevenLabs:ApiKey"];
if (string.IsNullOrWhiteSpace(elevenLabsApiKey))
{
    startupLogger.LogWarning(
        "ElevenLabs API key is not configured. Set ELEVENLABS_API_KEY (environment variable) or dotnet user-secrets for project SmartAssistApi.csproj.");
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
