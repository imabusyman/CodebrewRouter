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

Blaze.LlmGateway is a .NET 10 LLM routing proxy built around `Microsoft.Extensions.AI` (MEAI). The API project exposes an OpenAI-compatible `POST /v1/chat/completions` endpoint and streams Server-Sent Events via `IChatClient.GetStreamingResponseAsync(...)`.

**Project responsibilities:**

| Project | Role |
|---|---|
| `Core` | Domain types only — `RouteDestination` enum, `LlmGatewayOptions` config. Zero external deps. |
| `Infrastructure` | Provider registrations, routing middleware (`LlmRoutingChatClient`), MCP integration, routing strategies. All MEAI pipeline components live here. |
| `Api` | `Program.cs` wires DI via extension methods, registers the MCP hosted service, and hosts the SSE streaming endpoint. |
| `AppHost` | Aspire orchestrator — provisions Ollama container and GitHub Models, injects secrets as `LlmGateway__Providers__...` env vars. |
| `ServiceDefaults` | Shared Aspire conventions — OpenTelemetry, HTTP resilience, service discovery. |
| `Tests` | xUnit + Moq. 95% coverage target. |
| `Benchmarks` | BenchmarkDotNet for provider latency and routing overhead. |

**MEAI middleware pipeline (outermost → innermost):**

```text
McpToolDelegatingClient          ← unkeyed IChatClient; injects MCP tools into ChatOptions
  └── LlmRoutingChatClient       ← resolves RouteDestination via IRoutingStrategy
        └── [Keyed IChatClient].UseFunctionInvocation()   ← per-provider, actual model call
```

`AddLlmInfrastructure()` assembles this pipeline. `AddLlmProviders()` registers the 9 keyed provider clients. Both live in `InfrastructureServiceExtensions`.

**Two-stage routing:**

1. `OllamaMetaRoutingStrategy` sends the last user message to the keyed `"Ollama"` client with `MaxOutputTokens = 10, Temperature = 0` and parses the response as a `RouteDestination` enum name (exact match first, then substring scan).
2. On any failure or unrecognized response, falls back to `KeywordRoutingStrategy`, which defaults to `AzureFoundry`.

**Configuration:** `LlmGatewayOptions` is bound from the `"LlmGateway"` config section (`LlmGatewayOptions.SectionName`). All provider settings (API keys, endpoints, model names) live under `LlmGateway:Providers:<ProviderName>`. `RoutingOptions.RouterModel` (default: `"router"`) and `RoutingOptions.FallbackDestination` are also configurable.

**MCP:** `McpConnectionManager` is registered as both an `IHostedService` and a directly resolved singleton (via `sp.GetServices<IHostedService>().OfType<McpConnectionManager>().First()`). The `microsoft-learn` MCP server is wired in `Program.cs` via `npx -y @microsoft/mcp-server-microsoft-learn` (Stdio transport).

## Key conventions

- Use `Microsoft.Extensions.AI` abstractions for all LLM work — `IChatClient`, `ChatMessage`, `ChatOptions`, `ChatRole`. No raw `HttpClient` to LLM APIs.
- New pipeline middleware must inherit from `DelegatingChatClient`. **Note:** `LlmRoutingChatClient` and `McpToolDelegatingClient` currently implement `IChatClient` directly — this is a known gap; refactor when touching those files.
- Resolve providers via keyed DI: `serviceProvider.GetKeyedService<IChatClient>(destination.ToString())`. Provider keys must exactly match `RouteDestination` enum names.
- Keep `Program.cs` clean — all DI logic belongs in extension methods in `Infrastructure`.
- Each keyed provider is individually wrapped with `.AsBuilder().UseFunctionInvocation().Build()`. Function invocation is per-provider, not a shared pipeline layer.
- Provider SDK mappings (must be followed exactly):
  - `AzureFoundry` / `FoundryLocal` → `AzureOpenAIClient` → `.GetChatClient(model).AsIChatClient()`. `AzureFoundry` uses `DefaultAzureCredential` when `ApiKey` is absent; `FoundryLocal` uses `"notneeded"` as the API key.
  - `Ollama` / `OllamaBackup` / `OllamaLocal` → `OllamaApiClient` (cast to `IChatClient`) → `.AsBuilder().UseFunctionInvocation().Build()`
  - `GithubCopilot` / `GithubModels` / `OpenRouter` → `OpenAIClient` with custom `Endpoint` → `.GetChatClient(model).AsIChatClient()`
  - `Gemini` → `Google.GenAI.Client` → `.AsIChatClient(model)`
- The streaming endpoint must emit SSE chunks and a final `data: [DONE]\n\n` line. Use `GetStreamingResponseAsync` (current MEAI API — `CompleteAsync`/`CompleteStreamingAsync` no longer exist).
- All secrets are set on `Blaze.LlmGateway.AppHost` via `dotnet user-secrets`, not the API project. Keys: `Parameters:azure-foundry-endpoint`, `Parameters:azure-foundry-api-key`, `Parameters:github-copilot-api-key`, `Parameters:gemini-api-key`, `Parameters:openrouter-api-key`, `Parameters:github-models-api-key`, `Parameters:syncfusion-license-key`.
- Use `Blaze.LlmGateway.ServiceDefaults` shared extensions for host setup (OpenTelemetry, resilience, health checks) instead of duplicating.
- Code style: primary constructors, collection expressions (`[]`), nullable reference types enabled, `CancellationToken` propagated throughout.

## Known implementation gaps

- `LlmRoutingChatClient` and `McpToolDelegatingClient` implement `IChatClient` directly instead of inheriting `DelegatingChatClient` — refactor when touching these files.
- `McpConnectionManager.StartAsync()` is a placeholder — MCP tool connections not fully wired.
- `McpToolDelegatingClient.AppendMcpTools` needs full mapping to `HostedMcpServerTool` instances.
- No circuit breaker — most pressing resilience gap.
- Streaming failover — mid-stream failure handling not implemented.
- `Blaze.LlmGateway.Web` — Blazor frontend scaffolded but not yet connected to the API.
