namespace Blaze.LlmGateway.Core.ModelCatalog;

public interface IModelAvailabilityRegistry
{
    IReadOnlyList<AvailableModel> GetModels(bool includeUnavailable = false);
    AvailableModel? FindModel(string modelId, bool includeUnavailable = false);
    bool IsProviderAvailable(string provider);
    string? GetProviderError(string provider);
}
