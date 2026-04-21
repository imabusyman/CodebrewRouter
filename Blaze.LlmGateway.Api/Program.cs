using System.Text.Json;
using Blaze.LlmGateway.Api;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

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

var app = builder.Build();

var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
startupLogger.LogInformation("🟢 Blaze.LlmGateway.Api starting up...");
startupLogger.LogDebug("  ├─ Environment: {Environment}", app.Environment.EnvironmentName);

// Enable OpenAPI/Swagger UI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    startupLogger.LogInformation("  ├─ OpenAPI/Swagger enabled at /openapi/v1.json");
}

// Register LiteLLM-compatible endpoints  
app.RegisterLiteLlmEndpoints();

app.MapDefaultEndpoints();

startupLogger.LogInformation("✅ Blaze.LlmGateway.Api startup complete");

app.Run();

// For testing via WebApplicationFactory
public partial class Program { }

