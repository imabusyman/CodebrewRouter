using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Api;

public sealed class ModelCatalogService(
    IOptions<LlmGatewayOptions> options,
    AzureFoundryModelDiscovery modelDiscovery,
    ILogger<ModelCatalogService> logger) : IModelCatalog
{
    public async Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var configured = await GetDiscoveredAndConfiguredModelsAsync(cancellationToken);

        IReadOnlyList<AvailableModel> result = configured
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(model => model.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return result;
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

    private async Task<IReadOnlyList<AvailableModel>> GetDiscoveredAndConfiguredModelsAsync(CancellationToken cancellationToken)
    {
        var models = new List<AvailableModel>();

        // Query Azure Foundry for dynamically available models
        var providers = options.Value.Providers;
        var azureFoundryOpts = providers.AzureFoundry;
        if (!string.IsNullOrWhiteSpace(azureFoundryOpts.Endpoint))
        {
            logger.LogDebug("Querying Azure Foundry for available models");
            var discoveredModels = await modelDiscovery.DiscoverModelsAsync(
                azureFoundryOpts.Endpoint,
                azureFoundryOpts.ApiKey,
                cancellationToken);
            models.AddRange(discoveredModels);
        }

        // Add configured models (Foundry Local, CodebrewRouter virtual model)
        var configuredModels = GetConfiguredModels();
        models.AddRange(configuredModels);

        return models;
    }

    private IReadOnlyList<AvailableModel> GetConfiguredModels()
    {
        var providers = options.Value.Providers;
        var cbr = options.Value.CodebrewRouter;

        AvailableModel?[] configuredModels =
        [
            CreateConfiguredModel(providers.FoundryLocal.Model, "FoundryLocal", "openai", providers.FoundryLocal.Endpoint),
            cbr.Enabled && !string.IsNullOrWhiteSpace(cbr.ModelId)
                ? new AvailableModel(cbr.ModelId, "CodebrewRouter", "codebrew", "virtual")
                : null
        ];

        return configuredModels.Where(model => model is not null).Cast<AvailableModel>().ToArray();
    }

    private static AvailableModel? CreateConfiguredModel(string model, string provider, string ownedBy, string? endpoint = null)
        => string.IsNullOrWhiteSpace(model)
            ? null
            : new AvailableModel(model, provider, ownedBy, "configured", endpoint);
}
