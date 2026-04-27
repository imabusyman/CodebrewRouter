using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Api;

public sealed class ModelCatalogService(
    IModelAvailabilityRegistry availabilityRegistry,
    ILogger<ModelCatalogService> logger) : IModelCatalog
{
    public async Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        IReadOnlyList<AvailableModel> result = availabilityRegistry.GetModels()
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

        var model = availabilityRegistry.FindModel(modelId);
        if (model is not null)
        {
            logger.LogDebug("Resolved available model {ModelId} from availability registry", modelId);
        }

        return model;
    }
}
