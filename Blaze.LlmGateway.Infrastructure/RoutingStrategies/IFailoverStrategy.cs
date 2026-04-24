using Blaze.LlmGateway.Core;

namespace Blaze.LlmGateway.Infrastructure.RoutingStrategies;

/// <summary>
/// Determines the fallback chain for a provider destination when the primary fails.
/// </summary>
public interface IFailoverStrategy
{
    /// <summary>
    /// Get the failover chain for a given destination.
    /// Returns ordered list of fallback providers to try if the primary fails.
    /// </summary>
    IReadOnlyList<RouteDestination> GetFailoverChainAsync(RouteDestination primary);
}
