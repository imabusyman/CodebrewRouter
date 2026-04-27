// ============================================================
// Blaze.LlmGateway AppHost — dev-time orchestration
//
// Dev providers:
//   • AzureFoundry   — Azure-hosted Foundry / Azure OpenAI
//   • FoundryLocal   — on-device Foundry Local (OpenAI-compatible at localhost:58484)
//   • GithubModels   — GitHub Models inference API
//   • OllamaLocal    — LAN Ollama router/classifier at 192.168.16.12:11434
//
// Set these via user-secrets on the AppHost project:
//
//   dotnet user-secrets set "Parameters:azure-foundry-endpoint" "<https://your-resource.openai.azure.com/>" --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:azure-foundry-api-key"  "<key>" --project Blaze.LlmGateway.AppHost
//   dotnet user-secrets set "Parameters:github-models-api-key"  "<PAT with models:read>" --project Blaze.LlmGateway.AppHost
//
// FoundryLocal endpoint override:
//   dotnet user-secrets set "LlmGateway:Providers:FoundryLocal:Endpoint" "http://<host-or-lan-ip>:58484" --project Blaze.LlmGateway.AppHost
//   The Foundry Local SDK binds via WebService.Urls (default "127.0.0.1:0" = random port).
//   For LAN access, start/bind Foundry Local on a non-loopback URL such as http://0.0.0.0:58484,
//   then point the gateway endpoint at the reachable host/IP.
//
// Gateway API network binding (LAN access):
//   By default the gateway listens only on localhost (http://localhost:5022).
//   To expose the gateway to your local network (e.g. for OpenWebUI on another machine):
//   dotnet user-secrets set "Gateway:ListenUrls" "http://0.0.0.0:5022" --project Blaze.LlmGateway.AppHost
//   or in appsettings.Development.json: { "Gateway": { "ListenUrls": "http://0.0.0.0:5022" } }
//   This sets ASPNETCORE_URLS on the API project — do NOT hardcode a private LAN IP.
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

// ── Parameter-based secrets and non-secret provider settings ──
var azureFoundryEndpoint = builder.AddParameter("azure-foundry-endpoint");
var azureFoundryApiKey   = builder.AddParameter("azure-foundry-api-key", secret: true);
var githubModelsApiKey   = builder.AddParameter("github-models-api-key", secret: true);
var azureFoundryEndpointAlias = builder.Configuration["COPILOT_FOUNDRY_AZURE_BASE_URL"];
var azureFoundryResponsesEndpoint = builder.Configuration.GetValue(
    "LlmGateway:Providers:AzureFoundry:ResponsesEndpoint",
    "https://codebrew-resource.services.ai.azure.com/api/projects/codebrew/openai/v1/responses");
var azureFoundryResponsesEndpointAlias = builder.Configuration["COPILOT_FOUNDRY_RESPONSES_ENDPOINT"];
var azureFoundryApiKeyAlias = builder.Configuration["COPILOT_AZURE_API_KEY"];
var azureFoundryModelAlias = builder.Configuration["COPILOT_FOUNDRY_DEFAULT_MODEL"]
    ?? builder.Configuration["COPILOT_FOUNDRY_GENERAL_MODEL"];
var foundryLocalEndpoint = builder.Configuration.GetValue(
    "LlmGateway:Providers:FoundryLocal:Endpoint",
    "http://127.0.0.1:58484");
var foundryLocalModel = builder.Configuration.GetValue(
    "LlmGateway:Providers:FoundryLocal:Model",
    "Phi-4-mini-instruct-cuda-gpu:5");
var ollamaLocalBaseUrl = builder.Configuration.GetValue(
    "LlmGateway:Providers:OllamaLocal:BaseUrl",
    "http://192.168.16.12:11434");
var ollamaLocalModel = builder.Configuration.GetValue(
    "LlmGateway:Providers:OllamaLocal:Model",
    "gemma4:e4b");

// Gateway API listen URLs — controls which interfaces/ports Kestrel binds to.
// Leave empty to use Kestrel defaults (localhost only from launchSettings.json).
// Set to "http://0.0.0.0:5022" to expose the gateway on all LAN interfaces.
var gatewayListenUrls = builder.Configuration.GetValue<string?>("Gateway:ListenUrls");

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
    .WithEnvironment("LlmGateway__Providers__GithubModels__ApiKey",   githubModelsApiKey)
    .WithEnvironment("LlmGateway__Providers__FoundryLocal__Endpoint", foundryLocalEndpoint)
    .WithEnvironment("LlmGateway__Providers__FoundryLocal__Model",    foundryLocalModel)
    .WithEnvironment("LlmGateway__Providers__OllamaLocal__BaseUrl",   ollamaLocalBaseUrl)
    .WithEnvironment("LlmGateway__Providers__OllamaLocal__Model",     ollamaLocalModel);

if (string.IsNullOrWhiteSpace(azureFoundryEndpointAlias))
{
    api.WithEnvironment("LlmGateway__Providers__AzureFoundry__Endpoint", azureFoundryEndpoint);
}
else
{
    api.WithEnvironment("LlmGateway__Providers__AzureFoundry__Endpoint", azureFoundryEndpointAlias);
}

api.WithEnvironment(
    "LlmGateway__Providers__AzureFoundry__ResponsesEndpoint",
    string.IsNullOrWhiteSpace(azureFoundryResponsesEndpointAlias)
        ? azureFoundryResponsesEndpoint
        : azureFoundryResponsesEndpointAlias);

if (string.IsNullOrWhiteSpace(azureFoundryApiKeyAlias))
{
    api.WithEnvironment("LlmGateway__Providers__AzureFoundry__ApiKey", azureFoundryApiKey);
}
else
{
    api.WithEnvironment("LlmGateway__Providers__AzureFoundry__ApiKey", azureFoundryApiKeyAlias);
}

if (!string.IsNullOrWhiteSpace(azureFoundryModelAlias))
{
    api.WithEnvironment("LlmGateway__Providers__AzureFoundry__Model", azureFoundryModelAlias);
}

// Override Kestrel bind URLs when Gateway:ListenUrls is set (e.g. for LAN exposure)
if (!string.IsNullOrWhiteSpace(gatewayListenUrls))
{
    api.WithEnvironment("ASPNETCORE_URLS", gatewayListenUrls);
    aspireLogger.LogInformation("  ├─ Gateway listen URLs overridden: {Urls}", gatewayListenUrls);
}

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

    var openWebUi = builder.AddContainer("openwebui", "ghcr.io/open-webui/open-webui", "v0.9.2")
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
