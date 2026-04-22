using System.Text.Json;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Api;

public sealed class ModelCatalogService(
    IOptions<LlmGatewayOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<ModelCatalogService> logger) : IModelCatalog
{
    public async Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var configured = GetConfiguredModels();
        var liveOllamaModels = await GetLiveOllamaModelsAsync(cancellationToken);

        return configured
            .Concat(liveOllamaModels)
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(model => model.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<AvailableModel?> FindByIdAsync(string modelId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var models = await GetAvailableModelsAsync(cancellationToken);
        return models.FirstOrDefault(model => string.Equals(model.Id, modelId, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<AvailableModel> GetConfiguredModels()
    {
        var providers = options.Value.Providers;

        AvailableModel?[] configuredModels =
        [
            CreateConfiguredModel(providers.AzureFoundry.Model, "AzureFoundry", "openai", providers.AzureFoundry.Endpoint),
            CreateConfiguredModel(providers.GithubCopilot.Model, "GithubCopilot", "openai", providers.GithubCopilot.Endpoint),
            CreateConfiguredModel(providers.Gemini.Model, "Gemini", "google"),
            CreateConfiguredModel(providers.OpenRouter.Model, "OpenRouter", "qwen", providers.OpenRouter.Endpoint),
            CreateConfiguredModel(providers.FoundryLocal.Model, "FoundryLocal", "openai", providers.FoundryLocal.Endpoint),
            CreateConfiguredModel(providers.GithubModels.Model, "GithubModels", "openai", providers.GithubModels.Endpoint),
            CreateConfiguredModel(providers.OllamaLocal.Model, "OllamaLocal", "ollama", providers.OllamaLocal.BaseUrl)
        ];

        return configuredModels.Where(model => model is not null).Cast<AvailableModel>().ToArray();
    }

    private async Task<IReadOnlyList<AvailableModel>> GetLiveOllamaModelsAsync(CancellationToken cancellationToken)
    {
        var baseUrl = options.Value.Providers.OllamaLocal.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return [];
        }

        try
        {
            using var client = httpClientFactory.CreateClient(nameof(ModelCatalogService));
            client.BaseAddress = new Uri(baseUrl);

            using var response = await client.GetAsync("/api/tags", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(contentStream, cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return modelsElement
                .EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => new AvailableModel(name!, "OllamaLocal", "ollama", "live", baseUrl))
                .ToArray();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to enumerate live Ollama models from {BaseUrl}", baseUrl);
            return [];
        }
    }

    private static AvailableModel? CreateConfiguredModel(string model, string provider, string ownedBy, string? endpoint = null)
        => string.IsNullOrWhiteSpace(model)
            ? null
            : new AvailableModel(model, provider, ownedBy, "configured", endpoint);
}
