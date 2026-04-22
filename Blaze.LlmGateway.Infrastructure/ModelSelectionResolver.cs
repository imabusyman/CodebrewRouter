using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;

namespace Blaze.LlmGateway.Infrastructure;

public sealed class ModelSelectionResolver(
    IServiceProvider serviceProvider,
    IModelCatalog modelCatalog,
    ILogger<ModelSelectionResolver> logger) : IModelSelectionResolver
{
    public async Task<IChatClient?> ResolveAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var model = await modelCatalog.FindByIdAsync(modelId, cancellationToken);
        if (model is null)
        {
            return null;
        }

        if (string.Equals(model.Provider, "OllamaLocal", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(model.Endpoint))
        {
            logger.LogDebug("Resolving dynamic Ollama client for model {ModelId}", modelId);
            return ((IChatClient)new OllamaApiClient(new Uri(model.Endpoint), model.Id))
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();
        }

        logger.LogDebug("Resolving keyed client {Provider} for model {ModelId}", model.Provider, modelId);
        return serviceProvider.GetKeyedService<IChatClient>(model.Provider);
    }
}
