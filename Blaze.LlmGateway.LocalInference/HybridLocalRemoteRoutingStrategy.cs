using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Hybrid routing strategy that intelligently selects between local Gemma inference and remote LLM providers.
///
/// Decision flow:
/// 1. If LocalInference is enabled AND model is healthy and available → route to LocalGemma
/// 2. Else if remote provider is available and online → route to remote provider
/// 3. Else → fallback to configured default destination
///
/// Health is determined by:
/// - LocalInferenceOptions.Enabled = true
/// - Model path is available (cached or local file)
/// - IModelDistributionProvider.EnsureModelAvailableAsync() does not throw
/// </summary>
public class HybridLocalRemoteRoutingStrategy(
    LocalInferenceOptions options,
    IModelDistributionProvider modelProvider,
    IRoutingStrategy fallbackStrategy,
    ILogger<HybridLocalRemoteRoutingStrategy> logger) : IRoutingStrategy
{
    public async Task<RouteDestination> ResolveAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Check if local Gemma is healthy and available
            if (options.Enabled && !string.IsNullOrWhiteSpace(options.ModelPath))
            {
                try
                {
                    var modelPath = await modelProvider.EnsureModelAvailableAsync(options.ModelPath, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(modelPath))
                    {
                        logger.LogDebug("Local Gemma model available at {ModelPath}; routing to LocalGemma.", modelPath);
                        return RouteDestination.LocalGemma;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Request was cancelled; propagate it
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to verify local Gemma model availability; falling back to remote provider.");
                }
            }

            // 2. Local model unavailable or disabled; fall back to remote provider strategy
            logger.LogDebug("Local Gemma unavailable or disabled; falling back to remote provider routing strategy.");
            return await fallbackStrategy.ResolveAsync(messages, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in hybrid routing strategy; falling back to default destination.");
            return await fallbackStrategy.ResolveAsync(messages, cancellationToken);
        }
    }
}

