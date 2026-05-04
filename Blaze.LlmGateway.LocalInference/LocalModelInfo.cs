namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Metadata about an available local LLM model.
/// </summary>
public class LocalModelInfo
{
    /// <summary>
    /// The model identifier (e.g., "gemma-2b-it", "mistral-7b").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The absolute local file path to the model.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// The model type/family (e.g., "gemma", "mistral", "llama").
    /// </summary>
    public required string ModelType { get; init; }

    /// <summary>
    /// When the model was last verified to be available (loaded or checked).
    /// </summary>
    public required DateTime LoadedAtUtc { get; init; }

    /// <summary>
    /// Total size of the model file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Optional SHA256 checksum of the model file.
    /// </summary>
    public string? FileChecksum { get; init; }
}
