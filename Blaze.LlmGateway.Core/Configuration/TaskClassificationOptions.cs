namespace Blaze.LlmGateway.Core.Configuration;

/// <summary>
/// Configuration for the gemma4:e4b-powered task classification used by
/// <c>OllamaTaskClassifier</c>. Binds from <c>LlmGateway:TaskClassification</c> in appsettings.
/// </summary>
public class TaskClassificationOptions
{
    /// <summary>
    /// When false, task classification falls back to keyword-only matching.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum tokens the classifier model may produce. Task types are single words,
    /// so this should be quite small. Default: 50.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 50;

    /// <summary>
    /// Sampling temperature for the classifier model. Use 0 for deterministic classification.
    /// </summary>
    public float Temperature { get; set; } = 0f;

    /// <summary>
    /// How long to leave the classifier circuit open after a failure before retrying.
    /// Mirrors <c>GemmaPromptCleaner</c>. Default: 5 minutes.
    /// </summary>
    public int CooldownMinutes { get; set; } = 5;
}
