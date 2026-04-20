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

## Local Development Secrets

All provider credentials are injected via Aspire parameters. Set them once on the AppHost project:

```bash
dotnet user-secrets set "Parameters:azure-foundry-endpoint"  "<https://your-resource.openai.azure.com/>" --project Blaze.LlmGateway.AppHost
dotnet user-secrets set "Parameters:azure-foundry-api-key"   "<key>"   --project Blaze.LlmGateway.AppHost
dotnet user-secrets set "Parameters:github-copilot-api-key"  "<token>" --project Blaze.LlmGateway.AppHost
dotnet user-secrets set "Parameters:gemini-api-key"          "<key>"   --project Blaze.LlmGateway.AppHost
dotnet user-secrets set "Parameters:openrouter-api-key"      "<key>"   --project Blaze.LlmGateway.AppHost
dotnet user-secrets set "Parameters:github-models-api-key"   "<PAT>"   --project Blaze.LlmGateway.AppHost
dotnet user-secrets set "Parameters:syncfusion-license-key"  "<key>"   --project Blaze.LlmGateway.AppHost
```

## Architecture

### Project Responsibilities

| Project | Role |
|---|---|
| `Core` | Domain types only — `RouteDestination` enum, `LlmGatewayOptions` config classes. Zero external deps. |
| `Infrastructure` | Routing middleware, MCP integration, routing strategies. All MEAI pipeline components live here. |
| `Api` | `Program.cs` wires DI, registers providers via extension methods, exposes the SSE endpoint. |
| `Web` | Blazor Server frontend using Syncfusion components. References the API via Aspire service discovery. |
| `AppHost` | .NET Aspire orchestration — provisions Ollama container, GitHub Models, Azure Foundry Local, and wires secrets as environment variables. |
| `ServiceDefaults` | Shared Aspire conventions — OpenTelemetry, HTTP resilience, service discovery. |
| `Tests` | xUnit + Moq unit tests. 95% coverage target. |
| `Benchmarks` | BenchmarkDotNet for provider latency and routing overhead. |

### MEAI Middleware Pipeline (outermost → innermost)

```
McpToolDelegatingClient       ← injects MCP tools into ChatOptions (unkeyed IChatClient)
  └── LlmRoutingChatClient    ← resolves target provider via IRoutingStrategy
        └── [Keyed IChatClient].UseFunctionInvocation()  ← per-provider, actual model call
```

`FunctionInvokingChatClient` is registered individually on each keyed provider via `.AsBuilder().UseFunctionInvocation().Build()`, not as a shared pipeline layer. The unkeyed `IChatClient` registered in `AddLlmInfrastructure` is the `McpToolDelegatingClient` wrapping `LlmRoutingChatClient`.

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
3. **Streaming by default.** The `/v1/chat/completions` endpoint must use `GetStreamingResponseAsync` (the current MEAI API) and SSE. The old `CompleteAsync`/`CompleteStreamingAsync` names no longer exist.
4. **Keyed DI** for all provider resolution. Use `IServiceProvider.GetKeyedService<IChatClient>("ProviderName")` inside router middleware.
5. **Keep `Program.cs` clean.** Extract DI setup into extension methods.
6. **Code style:** Primary constructors, collection expressions (`[]`), nullable reference types enabled, `CancellationToken` propagated throughout.

## Known Incomplete Areas

- `McpConnectionManager.StartAsync()` — placeholder; MCP tool connections not fully wired.
- `McpToolDelegatingClient.AppendMcpTools` — needs mapping to `HostedMcpServerTool` instances.
- `LlmRoutingChatClient` and `McpToolDelegatingClient` — should inherit `DelegatingChatClient` (currently implement `IChatClient` directly).
- No circuit breaker — most pressing resilience gap.
- Streaming failover — mid-stream failure handling not yet implemented.
- `Blaze.LlmGateway.Web` — Blazor frontend scaffolded but not yet connected to the API (no HTTP client or chat UI wired up).

## Squad Orchestration

This repository ships two complementary squad execution paths (ADR-0009 + ADR-0010):

### Phased Conductor (human-gated)
- **Command:** `/agent squad`
- **Execution:** Sequential phases with human gates between each boundary (Planner → Architect → Coder → Tester → Reviewer → Security-Review).
- **Use when:** Task is exploratory, high-risk, or you want human feedback at each phase.
- **Output:** `Docs/squad/runs/<ts>-<slug>/` with reasoning log + handoffs.

### Orchestrator (autonomous)
- **Command:** `/orchestrate --prd <path>`
- **Execution:** Autonomous PRD-driven loop: decompose → parallel worktrees → dispatch subagents → monitor → merge → quality-gate.
- **Use when:** PRD is complete, task decomposes into parallel non-overlapping streams, and you want fully autonomous execution.
- **Output:** Same layout as Conductor, plus `.worktrees/<task>/` for isolated development.

**Comparison:**
| Aspect | Conductor | Orchestrator |
|---|---|---|
| Gating | Human gates at each phase | Fully autonomous |
| Parallelism | Sequential phases | Parallel tasks per phase |
| Execution speed | Slower (human waits) | Faster (no waits) |
| Risk level | Lower (human feedback) | Higher (autonomous) |
| Clean-context review | Per-phase | Post-completion (full log) |

## Squad Guardrails

This repository ships a 9-agent Claude-powered development squad (ADR-0009 + ADR-0010). Source of truth: [`prompts/squad/`](./prompts/squad/). Path-scoped guardrails every squad specialist honors:

- [`prompts/squad/_shared/guardrails.instructions.md`](./prompts/squad/_shared/guardrails.instructions.md) — universal squad rules (MEAI law, streaming, keyed DI, structured-action tags, quality gate).
- [`prompts/squad/_shared/meai-infrastructure.instructions.md`](./prompts/squad/_shared/meai-infrastructure.instructions.md) — scoped to `Blaze.LlmGateway.Infrastructure/**`, `Blaze.LlmGateway.Api/**`, `Blaze.LlmGateway.Core/**`.
- [`prompts/squad/_shared/aspire-apphost.instructions.md`](./prompts/squad/_shared/aspire-apphost.instructions.md) — scoped to `Blaze.LlmGateway.AppHost/**`, `Blaze.LlmGateway.ServiceDefaults/**`.
- [`prompts/squad/_shared/tests.instructions.md`](./prompts/squad/_shared/tests.instructions.md) — scoped to `Blaze.LlmGateway.Tests/**`, `Blaze.LlmGateway.Benchmarks/**`.
- [`prompts/squad/_shared/adr.instructions.md`](./prompts/squad/_shared/adr.instructions.md) — scoped to `Docs/design/adr/**`.
- [`prompts/squad/_shared/cloud-egress.instructions.md`](./prompts/squad/_shared/cloud-egress.instructions.md) — ADR-0008 default-deny, scoped to every C# and `appsettings*.json`.
- [`prompts/squad/_shared/style.instructions.md`](./prompts/squad/_shared/style.instructions.md) — C# style + nullability + build gate.

Protocol and command references:

- Tag vocabulary: [`prompts/squad/protocol/structured-actions.md`](./prompts/squad/protocol/structured-actions.md).
- Handoff envelope: [`prompts/squad/protocol/handoff-envelope.schema.md`](./prompts/squad/protocol/handoff-envelope.schema.md).
- Reasoning log: [`prompts/squad/protocol/reasoning-log.schema.md`](./prompts/squad/protocol/reasoning-log.schema.md).
- Slash commands: `/squad-plan`, `/squad-implement`, `/squad-review`, `/squad-security` (Claude Code) or `/agent squad` (Copilot CLI after `copilot plugin install ./.github/plugins/squad`).

Edit under `prompts/squad/` then run `pwsh ./scripts/sync-squad.ps1` to regenerate the `.github/plugins/squad/` and `.claude/` copies. Never edit the generated copies by hand.
