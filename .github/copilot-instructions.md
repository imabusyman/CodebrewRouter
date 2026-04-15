# Copilot Instructions for Blaze.LlmGateway

## Build, test, and run commands

```powershell
# Build the full solution with warnings treated as errors
 dotnet build Blaze.LlmGateway.slnx --no-incremental -warnaserror

# Run the test project
 dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build

# Run tests with coverage
 dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --collect:"XPlat Code Coverage"

# Run a single test class
 dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --filter "FullyQualifiedName~LlmRoutingChatClientTests"

# Run a single test method
 dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --filter "FullyQualifiedName~Blaze.LlmGateway.Tests.OllamaMetaRoutingStrategyTests.ReturnsCorrectDestination_WhenRouterReturnsExactName"

# Run the API directly
 dotnet run --project Blaze.LlmGateway.Api

# Run the full Aspire app (recommended for local development)
 dotnet run --project Blaze.LlmGateway.AppHost

# Run benchmarks
 dotnet run --project Blaze.LlmGateway.Benchmarks --configuration Release
```

## High-level architecture

Blaze.LlmGateway is a .NET 10 LLM routing proxy built around `Microsoft.Extensions.AI`. The API project exposes an OpenAI-compatible `POST /v1/chat/completions` endpoint and streams Server-Sent Events by forwarding `IChatClient.GetStreamingResponseAsync(...)` output directly.

The core routing flow spans multiple projects:

- `Blaze.LlmGateway.Api` binds `LlmGatewayOptions`, registers provider clients and infrastructure, and hosts the streaming endpoint.
- `Blaze.LlmGateway.Infrastructure` contains the provider registrations, MCP integration, routing middleware, and routing strategies.
- `Blaze.LlmGateway.Core` defines shared route and configuration types used by the API and infrastructure.
- `Blaze.LlmGateway.AppHost` is the Aspire orchestrator that provisions local/remote dependencies, injects provider secrets as environment variables, and launches both the API and Web projects.
- `Blaze.LlmGateway.Web` is a Blazor Server UI with Syncfusion wired through Aspire, but it is currently scaffold-level and not yet connected to the API chat flow.

The unkeyed `IChatClient` registered in `AddLlmInfrastructure()` is the main application pipeline. Its current stack is:

```text
McpToolDelegatingClient
  -> LlmRoutingChatClient
    -> keyed provider IChatClient built with .AsBuilder().UseFunctionInvocation().Build()
```

Routing itself is two-stage:

1. `OllamaMetaRoutingStrategy` asks the keyed `"Ollama"` client to classify the last user message into a `RouteDestination`.
2. If the router fails or returns something unrecognized, `KeywordRoutingStrategy` falls back to keyword matching, defaulting to `AzureFoundry`.

Provider registrations live in `InfrastructureServiceExtensions.AddLlmProviders()`. The repo currently wires 9 keyed providers: `AzureFoundry`, `Ollama`, `OllamaBackup`, `GithubCopilot`, `Gemini`, `OpenRouter`, `FoundryLocal`, `GithubModels`, and `OllamaLocal`.

Aspire AppHost is the source of truth for local secret wiring and resource composition. It provisions Azure Foundry Local, GitHub Models resources, and an Ollama container, then maps secrets into `LlmGateway__Providers__...` environment variables consumed by the API options binding.

## Key conventions

- Use `Microsoft.Extensions.AI` abstractions for all LLM work. Do not add raw `HttpClient` calls to providers; use `IChatClient`, `ChatMessage`, `ChatOptions`, and `ChatRole`.
- New chat pipeline middleware should inherit from `DelegatingChatClient`. `McpToolDelegatingClient` already follows this pattern; routing-related changes should preserve the unkeyed pipeline assembled in `AddLlmInfrastructure()`.
- Resolve provider implementations through keyed DI with `IServiceProvider.GetKeyedService<IChatClient>(...)` or `GetRequiredKeyedService(...)`. Provider names are expected to match `RouteDestination.ToString()` values.
- Keep provider registration and pipeline assembly in extension methods, not inline in `Program.cs`.
- Each keyed provider is wrapped individually with `.AsBuilder().UseFunctionInvocation().Build()`. Tool invocation is a per-provider concern, not a separate global layer.
- Preserve the provider SDK mappings already used by the repository:
  - Azure Foundry / FoundryLocal -> `AzureOpenAIClient`
  - Ollama / OllamaBackup / OllamaLocal -> `OllamaApiClient`
  - GitHub Copilot / GitHub Models / OpenRouter -> `OpenAIClient` with custom endpoints
  - Gemini -> `Google.GenAI.Client` via `.AsIChatClient(...)`
- The API endpoint is streaming-first. Changes to `/v1/chat/completions` should continue emitting SSE frames and a final `data: [DONE]` line.
- AppHost user-secrets are expected on `Blaze.LlmGateway.AppHost`, not the API project. Existing docs list keys such as `Parameters:azure-foundry-endpoint`, `Parameters:azure-foundry-api-key`, `Parameters:github-copilot-api-key`, `Parameters:gemini-api-key`, `Parameters:openrouter-api-key`, `Parameters:github-models-api-key`, and `Parameters:syncfusion-license-key`.
- `Blaze.LlmGateway.ServiceDefaults` provides shared service discovery, resilience, health checks, and OpenTelemetry defaults for hosted services. Prefer those shared extensions instead of duplicating host setup.
- There is a repo-scoped custom agent at `.github/agents/llm-gateway-architect.agent.md`. For large architecture or routing changes, prefer using `/agent llm-gateway-architect`.
- Be aware of current implementation gaps documented in repo guidance: MCP connectivity is only partially wired, the Web app is mostly scaffolded, and resilience features like circuit breaking and streaming failover are not complete yet.
