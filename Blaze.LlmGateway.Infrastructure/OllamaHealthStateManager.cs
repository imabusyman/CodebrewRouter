using System;
using Microsoft.Extensions.Logging;
using Blaze.LlmGateway.Core.Routing;

namespace Blaze.LlmGateway.Infrastructure;

/// <summary>
/// Thread-safe implementation of <see cref="IOllamaHealthState"/> using a simple lock.
/// Tracks health of primary and fallback Ollama router endpoints.
/// Short-lived operations and no read dominance justify lock over ReaderWriterLockSlim.
/// </summary>
public sealed class OllamaHealthStateManager : IOllamaHealthState
{
    private readonly object _lock = new();
    private bool _isPrimaryHealthy = true;
    private bool _isFallbackHealthy = true;
    private string? _primaryEndpoint;
    private string? _fallbackEndpoint;
    private readonly ILogger<OllamaHealthStateManager> _logger;

    public OllamaHealthStateManager(ILogger<OllamaHealthStateManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the primary and fallback endpoints. Called once at DI time.
    /// </summary>
    public void SetEndpoints(string primaryEndpoint, string fallbackEndpoint)
    {
        lock (_lock)
        {
            _primaryEndpoint = primaryEndpoint;
            _fallbackEndpoint = fallbackEndpoint;
            _isPrimaryHealthy = true;
            _isFallbackHealthy = true;
        }
    }

    public (string healthyEndpoint, string fallbackEndpoint) GetHealthyEndpoint()
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_primaryEndpoint) || string.IsNullOrEmpty(_fallbackEndpoint))
            {
                throw new InvalidOperationException("Endpoints not initialized");
            }

            if (_isPrimaryHealthy)
                return (_primaryEndpoint, _fallbackEndpoint);

            if (_isFallbackHealthy)
                return (_fallbackEndpoint, _primaryEndpoint);

            // Both unhealthy; return primary (failover will try it and log the failure)
            return (_primaryEndpoint, _fallbackEndpoint);
        }
    }

    public void MarkEndpointUnhealthy(string endpoint, Exception ex)
    {
        lock (_lock)
        {
            if (endpoint == _primaryEndpoint)
            {
                _isPrimaryHealthy = false;
                _logger.LogWarning("Primary Ollama endpoint {Endpoint} marked unhealthy: {Exception}", endpoint, ex.Message);
            }
            else if (endpoint == _fallbackEndpoint)
            {
                _isFallbackHealthy = false;
                _logger.LogWarning("Fallback Ollama endpoint {Endpoint} marked unhealthy: {Exception}", endpoint, ex.Message);
            }
        }
    }

    public void MarkEndpointHealthy(string endpoint)
    {
        lock (_lock)
        {
            if (endpoint == _primaryEndpoint)
            {
                _isPrimaryHealthy = true;
                _logger.LogInformation("Primary Ollama endpoint {Endpoint} marked healthy", endpoint);
            }
            else if (endpoint == _fallbackEndpoint)
            {
                _isFallbackHealthy = true;
                _logger.LogInformation("Fallback Ollama endpoint {Endpoint} marked healthy", endpoint);
            }
        }
    }

    public (bool isPrimaryHealthy, bool isFallbackHealthy) GetHealthStatus()
    {
        lock (_lock)
        {
            return (_isPrimaryHealthy, _isFallbackHealthy);
        }
    }
}
