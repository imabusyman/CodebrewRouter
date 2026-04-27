using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Core.Configuration;
using Microsoft.Extensions.Options;

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
                Source: model.Source,
                Enabled: model.Enabled,
                ErrorMessage: model.ErrorMessage))
            .ToList();

        var response = new ModelsResponse(
            Object: "list",
            Data: models
        );

        return Results.Json(response);
    }

    /// <summary>Handle CodebrewRouter-specific model listing requests.</summary>
    public static async Task<IResult> HandleCodebrewRouterAsync(
        IModelCatalog modelCatalog,
        IModelAvailabilityRegistry availabilityRegistry,
        IOptions<LlmGatewayOptions> options,
        CancellationToken cancellationToken)
    {
        var codebrewRouter = options.Value.CodebrewRouter;
        if (!codebrewRouter.Enabled || string.IsNullOrWhiteSpace(codebrewRouter.ModelId))
        {
            return Results.NotFound(new ErrorResponse(
                new ErrorDetail(
                    "The codebrewRouter virtual model is disabled or not configured.",
                    "not_found",
                    "model_not_found")));
        }

        var providerKeys = codebrewRouter.FallbackRules.Values
            .SelectMany(providers => providers)
            .Where(provider => !string.IsNullOrWhiteSpace(provider))
            .Where(availabilityRegistry.IsProviderAvailable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var backingModels = (await modelCatalog.GetAvailableModelsAsync(cancellationToken))
            .Where(model => providerKeys.Contains(model.Provider))
            .Select(model => new CodebrewRouterBackingModel(
                Id: model.Id,
                Object: "model",
                Provider: model.Provider,
                OwnedBy: model.OwnedBy,
                Source: model.Source))
            .OrderBy(model => model.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availability = availabilityRegistry.FindModel(codebrewRouter.ModelId, includeUnavailable: true);
        var fallbackRules = codebrewRouter.FallbackRules
            .OrderBy(rule => rule.Key, StringComparer.OrdinalIgnoreCase)
            .Select(rule => new CodebrewRouterFallbackRule(
                TaskType: rule.Key,
                Providers: rule.Value
                    .Where(provider => !string.IsNullOrWhiteSpace(provider))
                    .Where(availabilityRegistry.IsProviderAvailable)
                    .ToArray()))
            .ToList();

        var response = new CodebrewRouterModelsResponse(
            Id: codebrewRouter.ModelId,
            Object: "model",
            Provider: "CodebrewRouter",
            OwnedBy: "codebrew",
            Source: "virtual",
            Enabled: availability?.Enabled ?? false,
            ErrorMessage: availability?.ErrorMessage,
            BackingModels: backingModels,
            FallbackRules: fallbackRules);

        return Results.Json(response);
    }
}
