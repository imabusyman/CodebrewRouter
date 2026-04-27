using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scalar.Aspire;

namespace Blaze.LlmGateway.AppHost;

public static class AppHostComposition
{
    public static DistributedApplication Build(string[] args)
    {
        var builder = DistributedApplication.CreateBuilder(args);

        var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var aspireLogger = loggerFactory.CreateLogger("Blaze.LlmGateway.AppHost");
        aspireLogger.LogInformation("🔵 Aspire Orchestration starting...");
        aspireLogger.LogDebug("  ├─ Environment: {Environment}", builder.Environment.EnvironmentName);
        aspireLogger.LogDebug("  ├─ Wiring resources and dependencies");

        // ── Parameter-based secrets and non-secret provider settings ──
        var azureFoundryEndpoint = builder.AddParameter("azure-foundry-endpoint");
        var azureFoundryApiKey = builder.AddParameter("azure-foundry-api-key", secret: true);
        var githubModelsApiKey = builder.AddParameter("github-models-api-key", secret: true);
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
        var foundryLocalApiKey = builder.Configuration.GetValue(
            "LlmGateway:Providers:FoundryLocal:ApiKey",
            "notneeded");
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

        // ── Foundry Local — connection resource injected into the API ─────────
        // The currently available Aspire Foundry hosting preview breaks AppHost startup
        // in this repo's package train. Keep the AppHost-driven reference/injection model
        // by exposing Foundry Local as a connection-string resource instead.
        var foundryLocalChat = builder.AddConnectionString(
            "foundryLocalChat",
            ReferenceExpression.Create($"Endpoint={foundryLocalEndpoint};ApiKey={foundryLocalApiKey}"));

        // ── GitHub Models ──
        var ghGpt4oMini = builder.AddGitHubModel("gh-gpt4o-mini", "openai/gpt-4o-mini")
            .WithApiKey(githubModelsApiKey);
        var ghPhi4Mini = builder.AddGitHubModel("gh-phi4-mini", "microsoft/phi-4-mini-instruct")
            .WithApiKey(githubModelsApiKey);

        // ── API project — wire all resources ──
        var api = builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
            .WithReference(foundryLocalChat)
            .WithReference(ghGpt4oMini)
            .WithReference(ghPhi4Mini)
            .WithEnvironment("LlmGateway__Providers__GithubModels__ApiKey", githubModelsApiKey)
            .WithEnvironment("LlmGateway__Providers__OllamaLocal__BaseUrl", ollamaLocalBaseUrl)
            .WithEnvironment("LlmGateway__Providers__OllamaLocal__Model", ollamaLocalModel);

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

        if (!string.IsNullOrWhiteSpace(gatewayListenUrls))
        {
            api.WithEnvironment("ASPNETCORE_URLS", gatewayListenUrls);
            aspireLogger.LogInformation("  ├─ Gateway listen URLs overridden: {Urls}", gatewayListenUrls);
        }

        api.WithUrl("/", "Gateway Home")
           .WithUrls(ctx =>
           {
               var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
               ctx.Urls.RemoveAll(u => !seen.Add($"{u.DisplayText ?? string.Empty}|{u.Url}"));
           });

        var enableOpenWebUi = builder.Configuration.GetValue("DevUI:OpenWebUI", defaultValue: true);

        if (enableOpenWebUi)
        {
            aspireLogger.LogInformation("  ├─ Open WebUI: enabled (requires Docker Desktop)");

            _ = builder.AddContainer("openwebui", "ghcr.io/open-webui/open-webui", "v0.9.2")
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
        }
        else
        {
            aspireLogger.LogInformation("  ├─ Open WebUI: disabled (set DevUI:OpenWebUI=true to enable)");
        }

        var enableAgentDevUi = builder.Configuration.GetValue("DevUI:AgentFramework", defaultValue: false);

        if (enableAgentDevUi)
        {
            aspireLogger.LogInformation("  ├─ Agent Framework DevUI: enabled (requires `pip install agent-framework-devui`)");

            var agentsDir = Path.Combine(AppContext.BaseDirectory, "devui-agents");

            _ = builder.AddExecutable(
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
        }
        else
        {
            aspireLogger.LogInformation("  ├─ Agent Framework DevUI: disabled (set DevUI:AgentFramework=true to enable)");
        }

        builder.AddScalarApiReference()
            .WithApiReference(api, "/openapi/v1.json");

        aspireLogger.LogDebug("  ├─ API project configured with GitHub Models references");
        aspireLogger.LogDebug("  ├─ Dev UI playground(s) resolved (see flags above)");
        aspireLogger.LogDebug("  └─ Scalar API Reference configured for dashboard");
        aspireLogger.LogInformation("✅ Aspire orchestration ready - building distributed app");

        return builder.Build();
    }
}
