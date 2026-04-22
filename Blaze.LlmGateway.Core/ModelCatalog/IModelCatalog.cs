namespace Blaze.LlmGateway.Core.ModelCatalog;

public interface IModelCatalog
{
    Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default);
    Task<AvailableModel?> FindByIdAsync(string modelId, CancellationToken cancellationToken = default);
}
