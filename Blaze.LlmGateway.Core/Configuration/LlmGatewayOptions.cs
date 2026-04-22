namespace Blaze.LlmGateway.Core.Configuration;

public class LlmGatewayOptions
{
    public const string SectionName = "LlmGateway";

    public ProvidersOptions Providers { get; set; } = new();
    public RoutingOptions Routing { get; set; } = new();
}

public class ProvidersOptions
{
    public AzureFoundryOptions AzureFoundry { get; set; } = new();
    public GitHubCopilotOptions GithubCopilot { get; set; } = new();
    public GeminiOptions Gemini { get; set; } = new();
    public OpenRouterOptions OpenRouter { get; set; } = new();
    public FoundryLocalOptions FoundryLocal { get; set; } = new();
    public GitHubModelsOptions GithubModels { get; set; } = new();
    public OllamaLocalOptions OllamaLocal { get; set; } = new();
}

public class AzureFoundryOptions
{
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "gpt-4o";
    /// <summary>Optional. If absent, DefaultAzureCredential is used.</summary>
    public string? ApiKey { get; set; }
}

public class GitHubCopilotOptions
{
    public string Endpoint { get; set; } = "https://api.githubcopilot.com";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o";
}

public class GeminiOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gemini-2.0-flash";
}

public class OpenRouterOptions
{
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "qwen/qwen3-235b-a22b:free";
}

public class FoundryLocalOptions
{
    public string Endpoint { get; set; } = "http://localhost:5273";
    public string Model { get; set; } = "";
    /// <summary>Foundry Local uses "notneeded" as the API key.</summary>
    public string ApiKey { get; set; } = "notneeded";
}

public class GitHubModelsOptions
{
    public string Endpoint { get; set; } = "https://models.inference.ai.azure.com";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
}

public class OllamaLocalOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3.2";
}

public class RoutingOptions
{
    /// <summary>Name of the Ollama model used to route requests (the meta-router).</summary>
    public string RouterModel { get; set; } = "router";
    /// <summary>Fallback destination when meta-routing fails.</summary>
    public string FallbackDestination { get; set; } = nameof(RouteDestination.AzureFoundry);
}
