namespace Blaze.LlmGateway.Core.Configuration;

/// <summary>
/// Configuration for the <c>codebrewRouter</c> virtual model.
/// Binds from <c>LlmGateway:CodebrewRouter</c> in appsettings.
/// </summary>
public class CodebrewRouterOptions
{
    /// <summary>When false, the virtual model is omitted from the model catalog and DI registration is skipped.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Model ID that appears in <c>GET /v1/models</c> and is matched by <c>ModelSelectionResolver</c>.</summary>
    public string ModelId { get; set; } = "codebrewRouter";

    /// <summary>
    /// Maps each <c>TaskType</c> name to an ordered list of provider DI keys to try in sequence.
    /// If a provider key is absent from keyed DI, it is skipped.
    /// If a provider throws, the next key in the list is tried.
    /// Falls back to <c>InnerClient</c> (AzureFoundry) when all entries are exhausted.
    /// </summary>
    public Dictionary<string, string[]> FallbackRules { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Reasoning"]             = ["AzureFoundry", "Gemini", "GithubCopilot", "GithubModels"],
        ["Coding"]                = ["GithubCopilot", "AzureFoundry", "GithubModels", "OllamaLocal"],
        ["Research"]              = ["AzureFoundry", "Gemini", "OpenRouter", "GithubModels"],
        ["VisionObjectDetection"] = ["Gemini", "AzureFoundry", "GithubCopilot"],
        ["Creative"]              = ["OpenRouter", "AzureFoundry", "GithubCopilot"],
        ["DataAnalysis"]          = ["AzureFoundry", "Gemini", "OpenRouter"],
        ["General"]               = ["AzureFoundry", "GithubModels", "OllamaLocal"],
    };
}
