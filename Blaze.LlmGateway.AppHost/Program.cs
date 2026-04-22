// ============================================================
// Set these once via user-secrets on the AppHost project:
//
//   dotnet user-secrets set "Parameters:azure-foundry-endpoint"   "<https://your-resource.openai.azure.com/>" --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:azure-foundry-api-key"    "<key>"   --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:github-copilot-api-key"   "<token>" --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:gemini-api-key"           "<key>"   --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:openrouter-api-key"       "<key>"   --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:syncfusion-license-key"   "<key>"   --project Blaze.LlmGateway.AppHost
//
// GitHub Models PAT (models:read scope) — stored automatically as "gh-gpt4o-mini-gh-apikey":
//   dotnet user-secrets set "Parameters:github-models-api-key"    "<PAT>"   --project Blaze.LlmGateway.AppHost
//
// In production supply the same names as environment variables or
// map them to Azure Key Vault references via Aspire's resource model.
// ============================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scalar.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

// Configure logging for Aspire orchestration
var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var aspireLogger = loggerFactory.CreateLogger("Blaze.LlmGateway.AppHost");
aspireLogger.LogInformation("🔵 Aspire Orchestration starting...");
aspireLogger.LogDebug("  ├─ Environment: {Environment}", builder.Environment.EnvironmentName);
aspireLogger.LogDebug("  ├─ Wiring resources and dependencies");

// ── Existing parameter-based secrets ──
var azureFoundryEndpoint  = builder.AddParameter("azure-foundry-endpoint");
var azureFoundryApiKey    = builder.AddParameter("azure-foundry-api-key",  secret: true);
var githubCopilotApiKey   = builder.AddParameter("github-copilot-api-key", secret: true);
var geminiApiKey          = builder.AddParameter("gemini-api-key",         secret: true);
var openRouterApiKey      = builder.AddParameter("openrouter-api-key",     secret: true);
var githubModelsApiKey    = builder.AddParameter("github-models-api-key",  secret: true);
var syncfusionLicenseKey  = builder.AddParameter("syncfusion-license-key", secret: true);

// ── Azure Foundry Local (optional: requires Docker) ──
// Note: RunAsFoundryLocal() requires IContainerRuntime (Docker daemon)
// Uncomment below if Docker is properly configured
// var aiFoundry = builder.AddAzureAIFoundry("ai-foundry").RunAsFoundryLocal();
// var foundryChat = aiFoundry.AddDeployment("foundry-chat", AIFoundryModel.Local.Phi4Mini);

// ── GitHub Models ──
var ghGpt4oMini = builder.AddGitHubModel("gh-gpt4o-mini", "openai/gpt-4o-mini")
    .WithApiKey(githubModelsApiKey);
var ghPhi4Mini = builder.AddGitHubModel("gh-phi4-mini", "microsoft/phi-4-mini-instruct")
    .WithApiKey(githubModelsApiKey);

// ── Local Ollama container (optional: requires Docker) ──
// Uncomment if you have Ollama Docker image available
// var ollamaLocal = builder.AddContainer("ollama-local", "ollama/ollama")
//     .WithEndpoint(port: 11434, targetPort: 11434, name: "ollama", scheme: "http")
//     .WithVolume("ollama-data", "/root/.ollama");

// ── API project — wire all resources ──
var api = builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
    // .WithReference(foundryChat)  // Commented out: Foundry Local requires Docker
    .WithReference(ghGpt4oMini)
    .WithReference(ghPhi4Mini)
    .WithEnvironment("LlmGateway__Providers__AzureFoundry__Endpoint", azureFoundryEndpoint)
    .WithEnvironment("LlmGateway__Providers__AzureFoundry__ApiKey",   azureFoundryApiKey)
    .WithEnvironment("LlmGateway__Providers__GithubCopilot__ApiKey",  githubCopilotApiKey)
    .WithEnvironment("LlmGateway__Providers__Gemini__ApiKey",         geminiApiKey)
    .WithEnvironment("LlmGateway__Providers__OpenRouter__ApiKey",     openRouterApiKey)
    .WithEnvironment("LlmGateway__Providers__GithubModels__ApiKey",   githubModelsApiKey);

// Add documentation links as custom URLs in the dashboard
api.WithUrl("/swagger", "Swagger UI")
   .WithUrl("/scalar", "Scalar API Reference");

// ── Dev UIs (Docker-based; optional) ──────────────────────────────────────
// These are pure dev-time playgrounds for exercising the OpenAI-compatible
// /v1/chat/completions endpoint with streaming. They are orchestrated as
// container / executable resources so they don't pollute Blaze.LlmGateway.Web,
// which is reserved for the real Blazor application.
//
// Toggle each via appsettings (AppHost) or env vars:
//   DevUI:OpenWebUI     (default true)   — Open WebUI chat playground
//   DevUI:AgentFramework (default false) — Microsoft Agent Framework DevUI (Python)
//
// Open WebUI requires Docker Desktop (or another container runtime) to be running.
var enableOpenWebUi = builder.Configuration.GetValue("DevUI:OpenWebUI", defaultValue: true);
var enableAgentDevUi = builder.Configuration.GetValue("DevUI:AgentFramework", defaultValue: false);

if (enableOpenWebUi)
{
    aspireLogger.LogInformation("  ├─ Open WebUI playground: enabled (container)");

    var openWebUi = builder.AddContainer("openwebui", "ghcr.io/open-webui/open-webui", "main")
        .WithHttpEndpoint(targetPort: 8080, name: "http")
        .WithVolume("blaze-openwebui-data", "/app/backend/data")
        // Point Open WebUI at our gateway's OpenAI-compatible surface.
        .WithEnvironment(ctx =>
        {
            var apiEndpoint = api.GetEndpoint("http");
            ctx.EnvironmentVariables["OPENAI_API_BASE_URL"] =
                ReferenceExpression.Create($"{apiEndpoint}/v1");
        })
        // Open WebUI always sends an Authorization: Bearer header; the gateway
        // ignores it today, but we set a deterministic dev token so logs/filters
        // can identify playground traffic if needed.
        .WithEnvironment("OPENAI_API_KEY", "sk-blaze-devui")
        .WithEnvironment("WEBUI_AUTH", "false")              // skip login for local dev
        .WithEnvironment("ENABLE_OLLAMA_API", "false")       // OpenAI path only
        .WithEnvironment("WEBUI_NAME", "Blaze LLM Gateway Playground")
        .WithEnvironment("DEFAULT_MODELS", "blaze-auto")
        .WaitFor(api);

    _ = openWebUi;
}
else
{
    aspireLogger.LogInformation("  ├─ Open WebUI playground: disabled (set DevUI:OpenWebUI=true to enable)");
}

if (enableAgentDevUi)
{
    // Microsoft Agent Framework DevUI ships as a Python package
    // (`pip install agent-framework-devui` exposes the `devui` CLI).
    // We orchestrate it as an executable resource; the user must have
    // Python 3.11+ and the package installed on PATH.
    aspireLogger.LogInformation("  ├─ Agent Framework DevUI: enabled (executable — requires `pip install agent-framework-devui`)");

    var devUi = builder.AddExecutable(
            name: "agent-devui",
            command: "devui",
            workingDirectory: AppContext.BaseDirectory,
            args: ["--host", "127.0.0.1", "--port", "8765"])
        .WithHttpEndpoint(port: 8765, targetPort: 8765, name: "http", isProxied: false)
        .WithEnvironment(ctx =>
        {
            var apiEndpoint = api.GetEndpoint("http");
            ctx.EnvironmentVariables["OPENAI_BASE_URL"] =
                ReferenceExpression.Create($"{apiEndpoint}/v1");
        })
        .WithEnvironment("OPENAI_API_KEY", "sk-blaze-devui")
        .WaitFor(api);

    _ = devUi;
}
else
{
    aspireLogger.LogInformation("  ├─ Agent Framework DevUI: disabled (set DevUI:AgentFramework=true to enable)");
}

// ── Web project (reserved for the real Blazor app; playgrounds above cover testing) ──
builder.AddProject<Projects.Blaze_LlmGateway_Web>("web")
    .WithReference(api)
    .WithEnvironment("Syncfusion__LicenseKey", syncfusionLicenseKey);

// ── Scalar API Reference integration for Aspire dashboard ──
builder.AddScalarApiReference()
    .WithApiReference(api, "/openapi/v1.json");

aspireLogger.LogDebug("  ├─ API project configured with GitHub Models references");
aspireLogger.LogDebug("  ├─ Dev UI playgrounds resolved (see flags above)");
aspireLogger.LogDebug("  ├─ Scalar API Reference configured for dashboard");
aspireLogger.LogDebug("  └─ Web project configured with Syncfusion license");
aspireLogger.LogInformation("✅ Aspire orchestration ready - building distributed app");

var app = builder.Build();

aspireLogger.LogInformation("🚀 Starting Aspire dashboard and services...");
aspireLogger.LogInformation("  📚 API Documentation available in Aspire Dashboard and via:");
aspireLogger.LogInformation("     • Swagger UI: http://localhost:5000/swagger");
aspireLogger.LogInformation("     • Scalar API Reference: http://localhost:5000/scalar");
app.Run();
