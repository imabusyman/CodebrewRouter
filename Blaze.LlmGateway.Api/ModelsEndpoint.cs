using Blaze.LlmGateway.Core.ModelCatalog;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Handler for GET /v1/models endpoint.
/// Returns available models and their providers.
/// </summary>
public static class ModelsEndpoint
{
    /// <summary>Handle model listing requests</summary>
    public static async Task<IResult> HandleAsync(IModelCatalog modelCatalog, CancellationToken cancellationToken)
    {
        var models = (await modelCatalog.GetAvailableModelsAsync(cancellationToken))
            .Select(model => new ModelInfo(
                Id: model.Id,
                Object: "model",
                Provider: model.Provider,
                OwnedBy: model.OwnedBy,
                Source: model.Source))
            .ToList();

        var response = new ModelsResponse(
            Object: "list",
            Data: models
        );

        return Results.Json(response);
    }
}
