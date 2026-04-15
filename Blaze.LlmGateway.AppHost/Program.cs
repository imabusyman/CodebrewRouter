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

using Aspire.Hosting.Azure;

var builder = DistributedApplication.CreateBuilder(args);

// ── Existing parameter-based secrets ──
var azureFoundryEndpoint  = builder.AddParameter("azure-foundry-endpoint");
var azureFoundryApiKey    = builder.AddParameter("azure-foundry-api-key",  secret: true);
var githubCopilotApiKey   = builder.AddParameter("github-copilot-api-key", secret: true);
var geminiApiKey          = builder.AddParameter("gemini-api-key",         secret: true);
var openRouterApiKey      = builder.AddParameter("openrouter-api-key",     secret: true);
var githubModelsApiKey    = builder.AddParameter("github-models-api-key",  secret: true);
var syncfusionLicenseKey  = builder.AddParameter("syncfusion-license-key", secret: true);

// ── Azure Foundry Local (runs Foundry Local as an Aspire-managed resource) ──
var aiFoundry = builder.AddAzureAIFoundry("ai-foundry").RunAsFoundryLocal();
var foundryChat = aiFoundry.AddDeployment("foundry-chat", AIFoundryModel.Local.Phi4Mini);

// ── GitHub Models ──
var ghGpt4oMini = builder.AddGitHubModel("gh-gpt4o-mini", "openai/gpt-4o-mini")
    .WithApiKey(githubModelsApiKey);
var ghPhi4Mini = builder.AddGitHubModel("gh-phi4-mini", "microsoft/phi-4-mini-instruct")
    .WithApiKey(githubModelsApiKey);

// ── Local Ollama container (backup for remote 192.168.16.56) ──
var ollamaLocal = builder.AddContainer("ollama-local", "ollama/ollama")
    .WithEndpoint(port: 11434, targetPort: 11434, name: "ollama", scheme: "http")
    .WithVolume("ollama-data", "/root/.ollama");

// ── API project — wire all resources ──
var api = builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
    .WithReference(foundryChat)
    .WithReference(ghGpt4oMini)
    .WithReference(ghPhi4Mini)
    .WithEnvironment("LlmGateway__Providers__AzureFoundry__Endpoint", azureFoundryEndpoint)
    .WithEnvironment("LlmGateway__Providers__AzureFoundry__ApiKey",   azureFoundryApiKey)
    .WithEnvironment("LlmGateway__Providers__GithubCopilot__ApiKey",  githubCopilotApiKey)
    .WithEnvironment("LlmGateway__Providers__Gemini__ApiKey",         geminiApiKey)
    .WithEnvironment("LlmGateway__Providers__OpenRouter__ApiKey",     openRouterApiKey)
    .WithEnvironment("LlmGateway__Providers__GithubModels__ApiKey",   githubModelsApiKey);

// ── Web project ──
builder.AddProject<Projects.Blaze_LlmGateway_Web>("web")
    .WithReference(api)
    .WithEnvironment("Syncfusion__LicenseKey", syncfusionLicenseKey);

builder.Build().Run();
