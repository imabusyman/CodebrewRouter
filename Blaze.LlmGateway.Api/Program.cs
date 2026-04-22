using System.Text.Json;
using Blaze.LlmGateway.Api;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Detect if running under Aspire and configure logging appropriately
var isRunningUnderAspire = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_ORCHESTRATION_ENABLED")) ||
                           !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")) ||
                           builder.Configuration["ASPIRE_RUNNING"] == "true";

// Configure logging: when under Aspire, logs go to Aspire console; otherwise standard console
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
if (!isRunningUnderAspire)
{
    // Add simple formatter for standalone runs
    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = false;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    });
}

builder.Services.Configure<LlmGatewayOptions>(
    builder.Configuration.GetSection(LlmGatewayOptions.SectionName));

// MCP integration disabled (microsoft-learn server connection issues)
// To re-enable: uncomment below and ensure @microsoft/mcp-server-microsoft-learn is available
// builder.Services.AddSingleton<IEnumerable<McpConnectionConfig>>([
//     new McpConnectionConfig
//     {
//         Id = "microsoft-learn",
//         TransportType = "Stdio",
//         Command = "npx",
//         Arguments = ["-y", "@microsoft/mcp-server-microsoft-learn"]
//     }
// ]);
// builder.Services.AddHostedService<McpConnectionManager>();
// builder.Services.AddSingleton(sp =>
//     sp.GetServices<IHostedService>().OfType<McpConnectionManager>().First());

// Keyed provider clients + routing pipeline (MCP disabled for now)
builder.Services.AddLlmProviders();
builder.Services.AddLlmInfrastructure();

// Configure OpenAPI/Swagger
builder.Services.AddOpenApi();

// Dev-only permissive CORS so playground UIs (Open WebUI, Agent Framework DevUI)
// running as sibling Aspire resources can call /v1/chat/completions from the browser.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("devui", policy => policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
    });
}

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
var aspireNote = isRunningUnderAspire ? " (via Aspire)" : " (standalone)";
startupLogger.LogInformation("🟢 Blaze.LlmGateway.Api starting up{AspireNote}...", aspireNote);
startupLogger.LogDebug("  ├─ Environment: {Environment}", app.Environment.EnvironmentName);
if (isRunningUnderAspire)
{
    startupLogger.LogDebug("  ├─ Running under Aspire orchestration - logs routed to Aspire console");
}
else
{
    startupLogger.LogDebug("  ├─ Standalone mode - logging to console");
}

// Enable OpenAPI/Swagger UI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("devui");
    startupLogger.LogInformation("  ├─ OpenAPI/Swagger enabled at /openapi/v1.json");
    startupLogger.LogInformation("  ├─ Permissive 'devui' CORS policy active (Development only)");
}

// Register LiteLLM-compatible endpoints  
app.RegisterLiteLlmEndpoints();

app.MapDefaultEndpoints();

startupLogger.LogInformation("✅ Blaze.LlmGateway.Api startup complete");

app.Run();

// For testing via WebApplicationFactory
public partial class Program { }

