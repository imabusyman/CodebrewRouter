using System.Text.Json;
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

// MCP connection manager (hosted service + singleton accessor)
builder.Services.AddSingleton<IEnumerable<McpConnectionConfig>>([
    new McpConnectionConfig
    {
        Id = "microsoft-learn",
        TransportType = "Stdio",
        Command = "npx",
        Arguments = ["-y", "@microsoft/mcp-server-microsoft-learn"]
    }
]);
builder.Services.AddHostedService<McpConnectionManager>();
builder.Services.AddSingleton(sp =>
    sp.GetServices<IHostedService>().OfType<McpConnectionManager>().First());

// Keyed provider clients + routing/MCP pipeline
builder.Services.AddLlmProviders();
builder.Services.AddLlmInfrastructure();

// Configure OpenAPI/Swagger
builder.Services.AddOpenApi();

var app = builder.Build();

// Enable OpenAPI/Swagger UI
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Register LiteLLM-compatible endpoints
app.MapPost("/v1/chat/completions", ChatCompletionsEndpoint.HandleAsync);
app.MapPost("/v1/completions", CompletionsEndpoint.HandleAsync);
app.MapGet("/v1/models", ModelsEndpoint.Handle);

app.MapDefaultEndpoints();

app.Run();
