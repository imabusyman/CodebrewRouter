# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Blaze.LlmGateway** is a .NET 10 intelligent LLM routing proxy built on `Microsoft.Extensions.AI` (MEAI). It exposes an OpenAI-compatible `POST /v1/chat/completions` streaming endpoint and routes requests across 9 LLM providers using a meta-routing strategy (Ollama-based classifier with keyword fallback).

## Commands

```bash
# Build entire solution (treat warnings as errors)
dotnet build --no-incremental -warnaserror

# Run all tests with coverage
dotnet test --no-build --collect:"XPlat Code Coverage"

# Run a single test class
dotnet test --no-build --filter "FullyQualifiedName~LlmRoutingChatClientTests"

# Run the API directly
dotnet run --project Blaze.LlmGateway.Api

# Run via Aspire orchestration (recommended for local dev)
dotnet run --project Blaze.LlmGateway.AppHost

# Run benchmarks
dotnet run --project Blaze.LlmGateway.Benchmarks --configuration Release
```

## Architecture

### Project Responsibilities

| Project | Role |
|---|---|
| `Core` | Domain types only — `RouteDestination` enum, `LlmGatewayOptions` config classes. Zero external deps. |
| `Infrastructure` | Routing middleware, MCP integration, routing strategies. All MEAI pipeline components live here. |
| `Api` | `Program.cs` wires DI, registers providers via extension methods, exposes the SSE endpoint. |
| `AppHost` | .NET Aspire orchestration — provisions Ollama container, GitHub Models, Azure Foundry Local. |
| `ServiceDefaults` | Shared Aspire conventions — OpenTelemetry, HTTP resilience, service discovery. |
| `Tests` | xUnit + Moq unit tests. 95% coverage target. |
| `Benchmarks` | BenchmarkDotNet for provider latency and routing overhead. |

### MEAI Middleware Pipeline (outermost → innermost)

```
LlmRoutingChatClient          ← resolves target provider via IRoutingStrategy
  └── McpToolDelegatingClient ← injects MCP tools into ChatOptions
        └── FunctionInvokingChatClient (.UseFunctionInvocation())
              └── [Keyed IChatClient]   ← actual provider
```

New middleware must inherit from `DelegatingChatClient` — never implement `IChatClient` directly.

### Routing

- **Primary:** `OllamaMetaRoutingStrategy` — sends the prompt to a local "router" model that classifies which `RouteDestination` to use.
- **Fallback:** `KeywordRoutingStrategy` — parses keywords from the last user message (e.g. "gemini" → Gemini, "azure" → AzureFoundry). Default destination: AzureFoundry.

### Providers (Keyed DI keys)

9 providers registered as keyed `IChatClient` services: `"AzureFoundry"`, `"Ollama"`, `"OllamaBackup"`, `"GithubCopilot"`, `"Gemini"`, `"OpenRouter"`, `"FoundryLocal"`, `"GithubModels"`, `"OllamaLocal"`.

SDK mappings (must be followed exactly):
- Azure Foundry / FoundryLocal → `AzureOpenAIClient` → `.AsChatClient()`
- Ollama variants → `OllamaApiClient` → `.AsChatClient()`
- GitHub Copilot, GitHub Models, OpenRouter → `OpenAIClient` (custom endpoint) → `.AsChatClient()`
- Gemini → `Google.GenAI` client → `.AsIChatClient()`

## Architectural Rules

1. **MEAI is the law.** Never use raw `HttpClient` for LLM calls. Always use `IChatClient`, `ChatMessage`, `ChatOptions`, `ChatRole` from `Microsoft.Extensions.AI`.
2. **MCP tool execution** is handled entirely by MEAI's `FunctionInvokingChatClient`. Never write custom tool-calling loops.
3. **Streaming by default.** The `/v1/chat/completions` endpoint must use `CompleteStreamingAsync` and SSE.
4. **Keyed DI** for all provider resolution. Use `IServiceProvider.GetKeyedService<IChatClient>("ProviderName")` inside router middleware.
5. **Keep `Program.cs` clean.** Extract DI setup into extension methods.
6. **Code style:** Primary constructors, collection expressions (`[]`), nullable reference types enabled, `CancellationToken` propagated throughout.

## Known Incomplete Areas

- `McpConnectionManager.StartAsync()` — placeholder; MCP tool connections not fully wired.
- `McpToolDelegatingClient.AppendMcpTools` — needs mapping to `HostedMcpServerTool` instances.
- `LlmRoutingChatClient` and `McpToolDelegatingClient` — should inherit `DelegatingChatClient` (currently implement `IChatClient` directly).
- No circuit breaker — most pressing resilience gap.
- Streaming failover — mid-stream failure handling not yet implemented.
