using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Core.TaskRouting;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Blaze.LlmGateway.Infrastructure.TaskClassification;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using System.ClientModel;

namespace Blaze.LlmGateway.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddLlmProviders(this IServiceCollection services)
    {
        // AzureFoundry — DefaultAzureCredential or ApiKey depending on config
        services.AddKeyedSingleton<IChatClient>("AzureFoundry", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.AzureFoundry;
            AzureOpenAIClient azureClient = string.IsNullOrWhiteSpace(opts.ApiKey)
                ? new AzureOpenAIClient(new Uri(opts.Endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(opts.Endpoint), new AzureKeyCredential(opts.ApiKey));
            return azureClient.GetChatClient(opts.Model).AsIChatClient()
                .AsBuilder().UseFunctionInvocation().Build();
        });

        // FoundryLocal — Azure Foundry Local (OpenAI-compatible at localhost)
        services.AddKeyedSingleton<IChatClient>("FoundryLocal", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.FoundryLocal;
            var client = new AzureOpenAIClient(
                new Uri(opts.Endpoint),
                new AzureKeyCredential(opts.ApiKey));
            return client.GetChatClient(opts.Model).AsIChatClient()
                .AsBuilder().UseFunctionInvocation().Build();
        });

        // OllamaLocal — local Ollama container (backup for remote server)
        services.AddKeyedSingleton<IChatClient>("OllamaLocal", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.OllamaLocal;
            return ((IChatClient)new OllamaApiClient(new Uri(opts.BaseUrl), opts.Model))
                .AsBuilder().UseFunctionInvocation().Build();
        });

        // GithubModels — GitHub Models API (OpenAI-compatible endpoint)
        services.AddKeyedSingleton<IChatClient>("GithubModels", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.GithubModels;
            if (string.IsNullOrWhiteSpace(opts.ApiKey))
            {
                throw new InvalidOperationException("GithubModels requires API key in LlmGateway:Providers:GithubModels:ApiKey");
            }
            // GitHub Models uses OpenAI-compatible API; use AzureOpenAIClient with custom endpoint
            var client = new AzureOpenAIClient(
                new Uri(opts.Endpoint),
                new AzureKeyCredential(opts.ApiKey));
            return client.GetChatClient(opts.Model).AsIChatClient()
                .AsBuilder().UseFunctionInvocation().Build();
        });

        return services;
    }

    public static IServiceCollection AddLlmInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IModelSelectionResolver, ModelSelectionResolver>();
        services.AddSingleton<KeywordRoutingStrategy>();
        services.AddSingleton<IRoutingStrategy>(sp =>
        {
            // Try to use OllamaLocal for meta-routing if available, otherwise use keyword-only routing
            var keywordFallback = sp.GetRequiredService<KeywordRoutingStrategy>();
            var logger = sp.GetRequiredService<ILogger<OllamaMetaRoutingStrategy>>();
            
            var routerClient = sp.GetKeyedService<IChatClient>("OllamaLocal");
            if (routerClient is not null)
            {
                return new OllamaMetaRoutingStrategy(routerClient, keywordFallback, logger);
            }
            
            // Ollama not available — use keyword routing directly
            logger.LogInformation("OllamaLocal not available; using keyword-only routing strategy");
            return keywordFallback;
        });

        // Register failover strategy with configuration
        services.AddSingleton<IFailoverStrategy>(sp =>
        {
            var strategy = new ConfiguredFailoverStrategy(
                sp.GetRequiredService<IOptions<LlmGatewayOptions>>(),
                sp.GetRequiredService<ILogger<ConfiguredFailoverStrategy>>());
            strategy.Initialize();
            return strategy;
        });

        services.AddSingleton<IChatClient>(sp =>
        {
            // Get the first available provider as fallback (for InnerClient wrapper)
            var fallback = sp.GetKeyedService<IChatClient>("GithubModels")
                          ?? sp.GetKeyedService<IChatClient>("AzureFoundry")
                          ?? sp.GetRequiredKeyedService<IChatClient>("FoundryLocal");
            
            var strategy = sp.GetRequiredService<IRoutingStrategy>();
            var failoverStrategy = sp.GetRequiredService<IFailoverStrategy>();
            var routerLogger = sp.GetRequiredService<ILogger<LlmRoutingChatClient>>();

            // MCP tool injection disabled (server connection issues)
            // To re-enable: uncomment McpConnectionManager registration in Program.cs
            // and uncomment the McpToolDelegatingClient wrapper below

            IChatClient router = new LlmRoutingChatClient(fallback, sp, strategy, failoverStrategy, routerLogger);
            // Wrap with MCP layer if available:
            // var mcpManager = sp.GetRequiredService<McpConnectionManager>();
            // var mcpLogger = sp.GetRequiredService<ILogger<McpToolDelegatingClient>>();
            // return new McpToolDelegatingClient(router, mcpManager, mcpLogger);

            return router;
        });

        // ── codebrewRouter virtual model ──────────────────────────────────────

        // Expose CodebrewRouterOptions from the nested LlmGatewayOptions property
        // so CodebrewRouterChatClient can receive IOptions<CodebrewRouterOptions> directly.
        services.AddSingleton<IOptions<CodebrewRouterOptions>>(sp =>
            Options.Create(sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.CodebrewRouter));

        // Task classifier: Ollama-backed with keyword fallback (zero-latency on Ollama outage)
        services.AddSingleton<KeywordTaskClassifier>();
        services.AddSingleton<ITaskClassifier>(sp => new OllamaTaskClassifier(
            sp.GetRequiredKeyedService<IChatClient>("OllamaLocal"),
            sp.GetRequiredService<KeywordTaskClassifier>(),
            sp.GetRequiredService<ILogger<OllamaTaskClassifier>>()));

        // codebrewRouter keyed client — resolved by ModelSelectionResolver when model = "codebrewRouter"
        services.AddKeyedSingleton<IChatClient>("CodebrewRouter", (sp, _) =>
            (IChatClient)new CodebrewRouterChatClient(
                sp.GetRequiredKeyedService<IChatClient>("AzureFoundry"),  // InnerClient hard fallback
                sp.GetRequiredService<ITaskClassifier>(),
                sp.GetRequiredService<IOptions<CodebrewRouterOptions>>(),
                sp,
                sp.GetRequiredService<ILogger<CodebrewRouterChatClient>>()));

        return services;
    }
}
