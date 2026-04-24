using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Api;

public sealed class ModelCatalogService(
    IOptions<LlmGatewayOptions> options) : IModelCatalog
{
    public Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        var configured = GetConfiguredModels();

        IReadOnlyList<AvailableModel> result = configured
            .GroupBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(model => model.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(result);
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
        var cbr = options.Value.CodebrewRouter;

        AvailableModel?[] configuredModels =
        [
            CreateConfiguredModel(providers.AzureFoundry.Model, "AzureFoundry", "openai", providers.AzureFoundry.Endpoint),
            CreateConfiguredModel(providers.FoundryLocal.Model, "FoundryLocal", "openai", providers.FoundryLocal.Endpoint),
            CreateConfiguredModel(providers.GithubModels.Model, "GithubModels", "openai", providers.GithubModels.Endpoint),
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
