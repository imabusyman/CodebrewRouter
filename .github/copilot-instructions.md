# Copilot Instructions for Blaze.LlmGateway

## Custom agents

Two repo-scoped agents are available:
- **`/agent llm-gateway-architect`** — for architecture decisions, pipeline changes, routing, resilience, and MCP work.
- **`/agent router`** — classifies intent/complexity and picks the best Copilot model. Run this first on complex tasks.

## Build, test, and run commands

```powershell
# Build the full solution with warnings treated as errors
dotnet build Blaze.LlmGateway.slnx --no-incremental -warnaserror

# Run all tests
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build

# Run tests with coverage
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --collect:"XPlat Code Coverage"

# Run a single test class
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --filter "FullyQualifiedName~LlmRoutingChatClientTests"

# Run a single test method
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --filter "FullyQualifiedName~Blaze.LlmGateway.Tests.OllamaMetaRoutingStrategyTests.ReturnsCorrectDestination_WhenRouterReturnsExactName"

# Run the full Aspire app (recommended for local development)
dotnet run --project Blaze.LlmGateway.AppHost

# Run the API directly (no Aspire orchestration)
dotnet run --project Blaze.LlmGateway.Api

# Run benchmarks
dotnet run --project Blaze.LlmGateway.Benchmarks --configuration Release
```

## High-level architecture

Blaze.LlmGateway is a .NET 10 LLM routing proxy built around `Microsoft.Extensions.AI` (MEAI). `Blaze.LlmGateway.Api` exposes an OpenAI-compatible `POST /v1/chat/completions` endpoint that converts the request body into `ChatMessage` instances, streams responses with `IChatClient.GetStreamingResponseAsync(...)`, and always terminates the SSE stream with `data: [DONE]`.

| Project | Role |
|---|---|
| `Blaze.LlmGateway.Core` | Domain types and configuration objects, especially `RouteDestination` and `LlmGatewayOptions`. |
| `Blaze.LlmGateway.Infrastructure` | Provider registrations, routing middleware, MCP integration, and routing strategies. |
| `Blaze.LlmGateway.Api` | Minimal API host that wires `LlmGatewayOptions`, MCP server config, provider registration, and the streaming endpoint. |
| `Blaze.LlmGateway.AppHost` | Aspire orchestrator that provisions Foundry Local, GitHub Models, and a local Ollama container, then injects configuration into the API and Web projects. |
| `Blaze.LlmGateway.ServiceDefaults` | Shared Aspire defaults for telemetry, resilience, health checks, and service discovery. |
| `Blaze.LlmGateway.Web` | Blazor Server UI using Syncfusion; currently scaffolded and referenced by AppHost, but not connected to the API yet. |
| `Blaze.LlmGateway.Tests` | xUnit + Moq coverage for routing, strategy behavior, and API integration points. |
| `Blaze.LlmGateway.Benchmarks` | BenchmarkDotNet harness for latency and routing-overhead measurements. |

### Request pipeline

The unkeyed `IChatClient` is assembled in `Blaze.LlmGateway.Infrastructure\InfrastructureServiceExtensions.cs` as:

```text
McpToolDelegatingClient
  -> LlmRoutingChatClient
       -> keyed provider client wrapped with .AsBuilder().UseFunctionInvocation().Build()
```

- `AddLlmProviders()` registers 9 keyed provider clients.
- `AddLlmInfrastructure()` registers `KeywordRoutingStrategy`, wraps it with `OllamaMetaRoutingStrategy`, then builds the unkeyed `IChatClient` used by the API.
- MCP tool injection happens before routing reaches the selected provider.
- MEAI function invocation is attached per provider, not once at the outer pipeline.

### Routing model

Routing is two-stage:

1. `OllamaMetaRoutingStrategy` sends the last user message to the keyed `"Ollama"` client with `MaxOutputTokens = 10` and `Temperature = 0`.
2. It expects a `RouteDestination` enum name, tries exact parsing first, then substring matching.
3. Any failure or unrecognized response falls back to `KeywordRoutingStrategy`.
4. `KeywordRoutingStrategy` defaults to `AzureFoundry` when no provider-specific hint is found.

The routing prompt currently biases providers this way:
- `AzureFoundry` — enterprise/business, Office 365, Azure-specific work
- `Ollama` — local/private tasks, coding help, general chat
- `OllamaBackup` — lower-priority or backup Ollama traffic
- `GithubCopilot` — code generation, debugging, GitHub-related tasks
- `Gemini` — multimodal, Google-service, and search-oriented tasks
- `OpenRouter` — creative writing, open-source model tasks, Qwen/general AI queries

### Provider and environment wiring

- Provider settings bind from the `LlmGateway` configuration section.
- AppHost injects provider settings as `LlmGateway__Providers__...` environment variables.
- Secrets are stored on `Blaze.LlmGateway.AppHost`, not the API project.
- `AppHost` provisions:
  - Azure AI Foundry Local via `AddAzureAIFoundry(...).RunAsFoundryLocal()`
  - GitHub Models via `AddGitHubModel(...)`
  - a local Ollama container via `AddContainer("ollama-local", "ollama/ollama")`

### MCP integration

- `Program.cs` registers MCP servers as `IEnumerable<McpConnectionConfig>`.
- `McpConnectionManager` is registered both as a hosted service and as a singleton accessor resolved from `IHostedService`.
- The default configured MCP server is `microsoft-learn`, started through `npx -y @microsoft/mcp-server-microsoft-learn`.
- `McpToolDelegatingClient` appends cached tools to `ChatOptions.Tools` before forwarding requests downstream.

## Key conventions

- Use `Microsoft.Extensions.AI` primitives for all model interactions: `IChatClient`, `ChatMessage`, `ChatOptions`, and `ChatRole`.
- Resolve providers by keyed DI using the `RouteDestination` enum name string. If you add a destination, keep enum values, DI keys, and router output text aligned.
- Keep provider registration and pipeline construction in `InfrastructureServiceExtensions`; `Program.cs` should stay minimal.
- Every keyed provider client is wrapped with `.AsBuilder().UseFunctionInvocation().Build()` individually.
- Follow the existing provider SDK mapping:
  - `AzureFoundry` and `FoundryLocal` use `AzureOpenAIClient`
  - `Ollama`, `OllamaBackup`, and `OllamaLocal` use `OllamaApiClient`
  - `GithubCopilot`, `GithubModels`, and `OpenRouter` use `OpenAIClient` with custom endpoints
  - `Gemini` uses `Google.GenAI.Client`
- The streaming endpoint is SSE-only and must keep the OpenAI-compatible chunk shape plus the final `data: [DONE]` terminator.
- Current MEAI usage is `GetResponseAsync(...)` and `GetStreamingResponseAsync(...)`; avoid older `CompleteAsync` / `CompleteStreamingAsync` APIs that appear in older docs.
- Tests in this repo mock `IChatClient.GetResponseAsync(...)` and use `FullyQualifiedName~...` filters for targeted execution.
- Code style already leans on primary constructors, collection expressions (`[]`), nullable reference types, and end-to-end `CancellationToken` propagation.

## Known implementation gaps

- `LlmRoutingChatClient` still implements `IChatClient` directly instead of inheriting `DelegatingChatClient`.
- `McpConnectionManager.StartAsync()` is still a placeholder and MCP connectivity is only partially wired.
- `McpToolDelegatingClient.AppendMcpTools(...)` appends cached tools directly and does not yet map to richer hosted MCP tool abstractions.
- `Blaze.LlmGateway.Web` is scaffolded but not yet connected to the API.
- Circuit breaking and mid-stream failover are not implemented yet.
