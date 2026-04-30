namespace Blaze.LlmGateway.Core.Configuration;

/// <summary>
/// Controls per-provider context window enforcement.
/// Binds from <c>LlmGateway:ContextSizing</c>.
/// </summary>
public class ContextSizingOptions
{
    /// <summary>When false, no token counting or compaction is attempted.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tokens reserved for the model's output when the caller does not specify
    /// <c>ChatOptions.MaxOutputTokens</c>.
    /// </summary>
    public int DefaultReservedOutputTokens { get; set; } = 1024;
}
