namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Event fired when the availability state of a local model changes.
/// </summary>
public class ModelAvailabilityChanged
{
    /// <summary>
    /// The model that changed availability.
    /// </summary>
    public required LocalModelInfo Model { get; init; }

    /// <summary>
    /// Whether the model was available before this change.
    /// </summary>
    public required bool WasAvailable { get; init; }

    /// <summary>
    /// Whether the model is available after this change.
    /// </summary>
    public required bool IsAvailable { get; init; }

    /// <summary>
    /// Human-readable reason for the availability change.
    /// Example: "File deleted", "TTL cache expired", "Download succeeded".
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// When the change was detected (UTC).
    /// </summary>
    public required DateTime ChangedAtUtc { get; init; }
}
