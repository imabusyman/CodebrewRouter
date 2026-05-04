namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Abstraction for downloading and caching model files, with support for checksums and circuit-breaker resilience.
/// </summary>
public interface IModelDistributionProvider
{
    /// <summary>
    /// Ensures a model is available locally (cached or downloaded) and returns the local file path.
    /// </summary>
    /// <param name="modelUrl">The local path or remote URL to the model file.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The absolute path to the local model file.</returns>
    /// <exception cref="InvalidOperationException">Thrown when model download or caching fails.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
    Task<string> EnsureModelAvailableAsync(string modelUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the cached local path for a model URL if available, without downloading.
    /// </summary>
    /// <param name="modelUrl">The remote URL to check.</param>
    /// <returns>The cached local path if available; null if the URL has never been downloaded.</returns>
    Task<string?> GetCachedModelPathAsync(string modelUrl);
}
