namespace Blaze.LlmGateway.Core.Routing;

/// <summary>
/// Thread-safe interface for querying and updating the health state of primary and fallback Ollama router endpoints.
/// Used by <c>OllamaFailoverClient</c> to make failover decisions.
/// </summary>
public interface IOllamaHealthState
{
    /// <summary>
    /// Returns the healthy endpoint and a fallback. If both are healthy, returns (primary, fallback).
    /// If only fallback is healthy, returns (fallback, primary). If neither is healthy, returns (primary, fallback) anyway.
    /// </summary>
    (string healthyEndpoint, string fallbackEndpoint) GetHealthyEndpoint();

    /// <summary>
    /// Marks an endpoint as unhealthy due to an exception.
    /// </summary>
    void MarkEndpointUnhealthy(string endpoint, Exception ex);

    /// <summary>
    /// Marks an endpoint as healthy (e.g., after a successful request).
    /// </summary>
    void MarkEndpointHealthy(string endpoint);

    /// <summary>
    /// Returns the current health status of both endpoints for diagnostics.
    /// </summary>
    (bool isPrimaryHealthy, bool isFallbackHealthy) GetHealthStatus();
}
