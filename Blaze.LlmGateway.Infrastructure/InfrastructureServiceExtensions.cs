using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Google.GenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OpenAI;
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

        // GithubCopilot — GitHub Copilot API
        services.AddKeyedSingleton<IChatClient>("GithubCopilot", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.GithubCopilot;
            var client = new OpenAIClient(
                new ApiKeyCredential(opts.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(opts.Endpoint) });
            return client.GetChatClient(opts.Model).AsIChatClient()
                .AsBuilder().UseFunctionInvocation().Build();
        });

        // Gemini
        services.AddKeyedSingleton<IChatClient>("Gemini", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.Gemini;
            return new Client(apiKey: opts.ApiKey).AsIChatClient(opts.Model)
                .AsBuilder().UseFunctionInvocation().Build();
        });

        // OpenRouter — Qwen3 free model
        services.AddKeyedSingleton<IChatClient>("OpenRouter", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.OpenRouter;
            var client = new OpenAIClient(
                new ApiKeyCredential(opts.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(opts.Endpoint) });
            return client.GetChatClient(opts.Model).AsIChatClient()
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

        // GithubModels — GitHub Models inference API (OpenAI-compatible)
        services.AddKeyedSingleton<IChatClient>("GithubModels", (sp, _) =>
        {
            var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.GithubModels;
            var client = new OpenAIClient(
                new ApiKeyCredential(opts.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(opts.Endpoint) });
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

        return services;
    }

    public static IServiceCollection AddLlmInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IModelSelectionResolver, ModelSelectionResolver>();
        services.AddSingleton<KeywordRoutingStrategy>();
        services.AddSingleton<IRoutingStrategy>(sp =>
        {
            var routerClient = sp.GetRequiredKeyedService<IChatClient>("OllamaLocal");
            var fallback = sp.GetRequiredService<KeywordRoutingStrategy>();
            var logger = sp.GetRequiredService<ILogger<OllamaMetaRoutingStrategy>>();
            return new OllamaMetaRoutingStrategy(routerClient, fallback, logger);
        });

        services.AddSingleton<IChatClient>(sp =>
        {
            var fallback = sp.GetRequiredKeyedService<IChatClient>("AzureFoundry");
            var strategy = sp.GetRequiredService<IRoutingStrategy>();
            var routerLogger = sp.GetRequiredService<ILogger<LlmRoutingChatClient>>();

            // MCP tool injection disabled (server connection issues)
            // To re-enable: uncomment McpConnectionManager registration in Program.cs
            // and uncomment the McpToolDelegatingClient wrapper below
            
            IChatClient router = new LlmRoutingChatClient(fallback, sp, strategy, routerLogger);
            // Wrap with MCP layer if available:
            // var mcpManager = sp.GetRequiredService<McpConnectionManager>();
            // var mcpLogger = sp.GetRequiredService<ILogger<McpToolDelegatingClient>>();
            // return new McpToolDelegatingClient(router, mcpManager, mcpLogger);
            
            return router;
        });

        return services;
    }
}
