using Microsoft.Extensions.Options;
using Blaze.LlmGateway.Core.Configuration;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Handler for GET /v1/models endpoint.
/// Returns available models and their providers.
/// </summary>
public static class ModelsEndpoint
{
    /// <summary>Handle model listing requests</summary>
    public static IResult Handle(IServiceProvider sp)
    {
        var models = new List<ModelInfo>();

        // Get the LLM gateway options
        var options = sp.GetRequiredService<IOptions<LlmGatewayOptions>>();
        var opts = options.Value.Providers;

        // Map provider names to their configured models
        var providerModels = new Dictionary<string, (string model, string ownedBy)>
        {
            ["AzureFoundry"] = (opts.AzureFoundry.Model, "openai"),
            ["Ollama"] = (opts.Ollama.Model, "meta"),
            ["OllamaBackup"] = (opts.OllamaBackup.Model, "meta"),
            ["GithubCopilot"] = (opts.GithubCopilot.Model, "openai"),
            ["Gemini"] = (opts.Gemini.Model, "google"),
            ["OpenRouter"] = (opts.OpenRouter.Model, "qwen"),
            ["FoundryLocal"] = (opts.FoundryLocal.Model, "openai"),
            ["GithubModels"] = (opts.GithubModels.Model, "openai"),
            ["OllamaLocal"] = (opts.OllamaLocal.Model, "meta")
        };

        // Collect all available models
        foreach (var (providerName, (model, ownedBy)) in providerModels)
        {
            if (!string.IsNullOrWhiteSpace(model))
            {
                models.Add(new ModelInfo(
                    Id: model,
                    Object: "model",
                    Provider: providerName,
                    OwnedBy: ownedBy
                ));
            }
        }

        var response = new ModelsResponse(
            Object: "list",
            Data: models
        );

        return Results.Json(response);
    }
}
