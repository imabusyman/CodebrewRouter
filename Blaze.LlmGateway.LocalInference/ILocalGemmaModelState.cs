namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Exposes local Gemma model load state to startup warmup without coupling to LLamaSharp internals.
/// </summary>
public interface ILocalGemmaModelState
{
    /// <summary>
    /// Configured local model path.
    /// </summary>
    string? ModelPath { get; }

    /// <summary>
    /// Whether the model was loaded into the local inference runtime.
    /// </summary>
    bool IsModelLoaded { get; }
}
