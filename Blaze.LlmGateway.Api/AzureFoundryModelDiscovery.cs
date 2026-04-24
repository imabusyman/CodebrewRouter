using System.Net.Http.Json;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Queries Azure Foundry to discover available models via the /openai/v1/models endpoint.
/// </summary>
public sealed class AzureFoundryModelDiscovery(
    HttpClient httpClient,
    ILogger<AzureFoundryModelDiscovery> logger)
{
    /// <summary>
    /// Query Azure Foundry for available models.
    /// </summary>
    public async Task<IReadOnlyList<AvailableModel>> DiscoverModelsAsync(
        string endpoint,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var modelsUrl = new Uri(new Uri(endpoint), "/openai/v1/models").ToString();
            logger.LogDebug("Querying Azure Foundry for available models at {Endpoint}", modelsUrl);

            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);

            // Add API key if provided
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Add("api-key", apiKey);
            }

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var modelsData = System.Text.Json.JsonSerializer.Deserialize<ModelsListResponse>(content);

            if (modelsData?.Data == null)
            {
                logger.LogWarning("Azure Foundry returned empty models list");
                return [];
            }

            var models = modelsData.Data
                .Where(m => !string.IsNullOrWhiteSpace(m.Id))
                .Select(m => new AvailableModel(
                    Id: m.Id!,
                    Provider: "AzureFoundry",
                    OwnedBy: m.OwnedBy ?? "openai",
                    Source: "discovered",
                    Endpoint: endpoint))
                .ToList();

            logger.LogInformation("Discovered {Count} models from Azure Foundry", models.Count);
            return models;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to discover models from Azure Foundry at {Endpoint}", endpoint);
            return [];
        }
    }

    /// <summary>Internal DTO for Azure Foundry model list response</summary>
    private sealed record ModelsListResponse(
        string? Object,
        List<ModelData>? Data);

    /// <summary>Internal DTO for a single model entry</summary>
    private sealed record ModelData(
        string? Id,
        string? Object,
        long? Created,
        string? OwnedBy);
}
