// ============================================================
// Blaze.LlmGateway AppHost — dev-time orchestration
//
// Dev providers (3 only):
//   • AzureFoundry   — Azure-hosted Foundry / Azure OpenAI
//   • FoundryLocal   — on-device Foundry Local (OpenAI-compatible at localhost:5273)
//   • GithubModels   — GitHub Models inference API
//
// Set these via user-secrets on the AppHost project:
//
//   dotnet user-secrets set "Parameters:azure-foundry-endpoint" "<https://your-resource.openai.azure.com/>" --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:azure-foundry-api-key"  "<key>" --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:github-models-api-key"  "<PAT with models:read>" --project Blaze.LlmGateway.AppHost
//
// Dev playgrounds (toggle via appsettings / env):
//   DevUI:OpenWebUI        (default true)  — generic OpenAI-compatible chat UI (requires Docker)
//   DevUI:AgentFramework   (default false) — Python Agent Framework DevUI (requires `pip install agent-framework-devui`)
// ============================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var aspireLogger = loggerFactory.CreateLogger("Blaze.LlmGateway.AppHost");
aspireLogger.LogInformation("🔵 Aspire Orchestration starting...");
aspireLogger.LogDebug("  ├─ Environment: {Environment}", builder.Environment.EnvironmentName);
aspireLogger.LogDebug("  ├─ Wiring resources and dependencies");

// ── Parameter-based secrets (3 provider set) ──
var azureFoundryEndpoint = builder.AddParameter("azure-foundry-endpoint");
var azureFoundryApiKey   = builder.AddParameter("azure-foundry-api-key", secret: true);
var githubModelsApiKey   = builder.AddParameter("github-models-api-key", secret: true);

// ── Azure Foundry Local (optional: requires Docker) ──
// Uncomment once Docker Desktop + IContainerRuntime are available.
// var aiFoundry = builder.AddAzureAIFoundry("ai-foundry").RunAsFoundryLocal();
// var foundryChat = aiFoundry.AddDeployment("foundry-chat", AIFoundryModel.Local.Phi4Mini);

// ── GitHub Models ──
var ghGpt4oMini = builder.AddGitHubModel("gh-gpt4o-mini", "openai/gpt-4o-mini")
    .WithApiKey(githubModelsApiKey);
var ghPhi4Mini = builder.AddGitHubModel("gh-phi4-mini", "microsoft/phi-4-mini-instruct")
    .WithApiKey(githubModelsApiKey);

// ── API project — wire all resources ──
var api = builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
    // .WithReference(foundryChat)  // Foundry Local requires Docker
    .WithReference(ghGpt4oMini)
    .WithReference(ghPhi4Mini)
    .WithEnvironment("LlmGateway__Providers__AzureFoundry__Endpoint", azureFoundryEndpoint)
    .WithEnvironment("LlmGateway__Providers__AzureFoundry__ApiKey",   azureFoundryApiKey)
    .WithEnvironment("LlmGateway__Providers__GithubModels__ApiKey",   githubModelsApiKey);

api.WithUrl("/", "Gateway Home")
   .WithUrls(ctx =>
   {
       // Aspire (and some integrations like Swashbuckle/Scalar) auto-register URL
       // annotations that can overlap with our explicit WithUrl entries. Keep the
       // first occurrence of each (Url, DisplayText) pair so the dashboard shows
       // each link exactly once.
       var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
       ctx.Urls.RemoveAll(u => !seen.Add($"{u.DisplayText ?? string.Empty}|{u.Url}"));
   });

// ── Dev UI: Open WebUI (container) — default playground ─────────────────────
// A ready-made OpenAI-compatible chat UI that points at the gateway.
// Prereq: Docker Desktop running. Disable by setting DevUI:OpenWebUI=false.
var enableOpenWebUi = builder.Configuration.GetValue("DevUI:OpenWebUI", defaultValue: true);

if (enableOpenWebUi)
{
    aspireLogger.LogInformation("  ├─ Open WebUI: enabled (requires Docker Desktop)");

    var openWebUi = builder.AddContainer("openwebui", "ghcr.io/open-webui/open-webui", "main")
        .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "http")
        .WithVolume("blaze-openwebui-data", "/app/backend/data")
        .WithEnvironment("WEBUI_AUTH", "False")
        .WithEnvironment("ENABLE_OLLAMA_API", "False")
        .WithEnvironment(ctx =>
        {
            var apiEndpoint = api.GetEndpoint("http");
            ctx.EnvironmentVariables["OPENAI_API_BASE_URL"] =
                ReferenceExpression.Create($"{apiEndpoint}/v1");
            ctx.EnvironmentVariables["OPENAI_API_KEY"] = "sk-blaze-openwebui";
        })
        .WaitFor(api);

    _ = openWebUi;
}
else
{
    aspireLogger.LogInformation("  ├─ Open WebUI: disabled (set DevUI:OpenWebUI=true to enable)");
}

// ── Dev UI: Microsoft Agent Framework DevUI (Python) — opt-in ───────────────
// DevUI hosts Python Agent Framework agents discovered from a directory.
// The gateway_agent under ./devui-agents routes through the gateway.
// Prereq: `pip install agent-framework-devui` so `devui` is on PATH.
var enableAgentDevUi = builder.Configuration.GetValue("DevUI:AgentFramework", defaultValue: false);

if (enableAgentDevUi)
{
    aspireLogger.LogInformation("  ├─ Agent Framework DevUI: enabled (requires `pip install agent-framework-devui`)");

    // devui-agents is copied next to the AppHost exe via <None> in the csproj.
    var agentsDir = Path.Combine(AppContext.BaseDirectory, "devui-agents");

    var devUi = builder.AddExecutable(
            name: "agent-devui",
            command: "devui",
            workingDirectory: AppContext.BaseDirectory,
            args: [agentsDir, "--port", "8765"])
        .WithHttpEndpoint(port: 8765, targetPort: 8765, name: "http", isProxied: false)
        .WithEnvironment(ctx =>
        {
            var apiEndpoint = api.GetEndpoint("http");
            ctx.EnvironmentVariables["OPENAI_BASE_URL"] =
                ReferenceExpression.Create($"{apiEndpoint}/v1");
        })
        .WithEnvironment("OPENAI_API_KEY", "sk-blaze-devui")
        .WithEnvironment("BLAZE_GATEWAY_MODEL", "codebrew-router")
        .WaitFor(api);

    _ = devUi;
}
else
{
    aspireLogger.LogInformation("  ├─ Agent Framework DevUI: disabled (set DevUI:AgentFramework=true to enable)");
}

// ── Scalar API Reference integration for Aspire dashboard ──
builder.AddScalarApiReference()
    .WithApiReference(api, "/openapi/v1.json");

aspireLogger.LogDebug("  ├─ API project configured with GitHub Models references");
aspireLogger.LogDebug("  ├─ Dev UI playground(s) resolved (see flags above)");
aspireLogger.LogDebug("  └─ Scalar API Reference configured for dashboard");
aspireLogger.LogInformation("✅ Aspire orchestration ready - building distributed app");

var app = builder.Build();

aspireLogger.LogInformation("🚀 Starting Aspire dashboard and services...");
aspireLogger.LogInformation("  📚 API Documentation available in Aspire Dashboard and via:");
aspireLogger.LogInformation("     • Swagger UI: http://localhost:5000/swagger");
aspireLogger.LogInformation("     • Scalar API Reference: http://localhost:5000/scalar");
app.Run();
