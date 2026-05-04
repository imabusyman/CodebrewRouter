namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Abstraction for checking and monitoring local LLM model availability.
/// Provides caching, observability, and structured availability information.
/// </summary>
public interface ILocalModelAvailability
{
    /// <summary>
    /// Gets the current availability status of a local model.
    /// May return cached result within TTL window.
    /// </summary>
    /// <param name="modelUrl">Local file path or remote URL to model.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>True if model is available; false otherwise.</returns>
    Task<bool> IsAvailableAsync(string modelUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a model is available, refreshing cache if necessary.
    /// May trigger download if model is remote and not cached.
    /// </summary>
    /// <param name="modelUrl">Local file path or remote URL to model.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>LocalModelInfo for the available model.</returns>
    /// <exception cref="InvalidOperationException">Thrown when model cannot be made available.</exception>
    Task<LocalModelInfo> EnsureAvailableAsync(string modelUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the currently cached availability information for a model.
    /// Does not perform any I/O or download attempts.
    /// </summary>
    /// <param name="modelUrl">Local file path or remote URL to model.</param>
    /// <returns>LocalModelInfo if cached; null if not available or not cached.</returns>
    LocalModelInfo? GetCachedAvailability(string modelUrl);

    /// <summary>
    /// Observes availability changes for all monitored models.
    /// Subscribe to receive notifications when model availability changes.
    /// </summary>
    /// <returns>Observable sequence of ModelAvailabilityChanged events.</returns>
    IObservable<ModelAvailabilityChanged> ObserveAvailabilityChanges();
}
