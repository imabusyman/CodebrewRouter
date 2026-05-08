namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Exposes local Gemma model load state to startup warmup without coupling to LLamaSharp internals.
/// </summary>
public interface ILocalGemmaModelState
{
    /// <summary>
    /// Configured local model path (or remote URL before resolution).
    /// </summary>
    string? ModelPath { get; }

    /// <summary>
    /// Whether the model was loaded into the local inference runtime.
    /// </summary>
    bool IsModelLoaded { get; }

    /// <summary>
    /// Ensures the model is downloaded (if needed) and loaded into the LLamaSharp runtime.
    /// Idempotent: returns immediately if already loaded.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token (does not apply the warmup timeout; use a linked CTS for that).</param>
    /// <param name="onModelFileReady">Optional callback invoked when the model file is local and LLamaSharp is about to load it. Use to advance progress state.</param>
    Task EnsureLoadedAsync(CancellationToken cancellationToken = default, Action? onModelFileReady = null);
}
