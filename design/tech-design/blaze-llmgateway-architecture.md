# Blaze.LlmGateway — Technical Design & Architecture

> **Document type:** Engineer-ready technical design blueprint
> **Status:** Proposed — all ADRs in [../adr](../adr) and all recommendations in this document are pending sign-off.
> **Last updated:** 2026-04-17
> **Companion docs:** [PRD](../../PRD/blaze-llmgateway-prd.md) (requirements), [planning draft](../../plan/llm-agent-platform-plan.md) (north-star direction), [CLAUDE.md](../../CLAUDE.md) (conventions)

---

## 1. Executive summary

Blaze.LlmGateway is today a **.NET 10 OpenAI-compatible routing proxy** fronting nine LLM providers over a Microsoft.Extensions.AI (MEAI) pipeline (`McpToolDelegatingClient → LlmRoutingChatClient → keyed provider`). This document captures the design for evolving it into an **internal-LAN LLM engine with an agent runtime** — the north-star described in [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md).

**The design rests on four internal planes inside a single primary API host:**

1. **Inference plane** — provider catalog, routing, resilience, streaming.
2. **Tool plane** — MCP connection lifecycle and tool policy.
3. **Agent plane** — session store, workflows, Microsoft Agent Framework and Azure Foundry agent adapters.
4. **Integration plane** — OpenAI-compatible API, admin API, Copilot sample, future Responses/A2A surfaces.

**Phase 1 cut line** (what ships before any agent-runtime work starts):

- Provider/model catalog (ADR-0002) replaces the `RouteDestination` enum.
- `LlmRoutingChatClient` refactored onto `DelegatingChatClient` with per-provider circuit breaker, fallback chain, pre-stream failover, and cloud-escalation gating.
- `McpToolDelegatingClient` upgraded to emit `HostedMcpServerTool` and support dynamic MCP server registration.
- OpenAI Chat Completions endpoint replaced with typed DTOs + spec-accurate streaming (ADR-0003).
- Durable session persistence (SQLite via EF Core) lands behind `ISessionStore` (ADR-0004), ready for the agent plane.
- Copilot CLI BYOM + one Copilot SDK sample (ADR-0007).

**Design decisions backing this document** (all in [../adr](../adr), all **Proposed**):

| # | Decision |
|---|---|
| [0001](../adr/0001-primary-host-boundary.md) | Gateway core + agent runtime co-hosted in the Api project, with strict project-level layering |
| [0002](../adr/0002-provider-identity-model.md) | Config-driven `ProviderDescriptor` + `ModelProfile`; migrate off `RouteDestination` enum |
| [0003](../adr/0003-northbound-api-surface.md) | Phase 1 = hardened OpenAI Chat Completions only; Responses/A2A deferred |
| [0004](../adr/0004-session-state-persistence.md) | SQLite + EF Core `ISessionStore`; swap to Postgres/Cosmos later |
| [0005](../adr/0005-local-runtime-compatibility.md) | LM Studio + llama.cpp as OpenAI-compat catalog entries, no specialized adapter |
| [0006](../adr/0006-azure-foundry-agents-integration.md) | `IAgentAdapter` pattern fronting Foundry hosted + local agents |
| [0007](../adr/0007-copilot-ecosystem-strategy.md) | Phase 1 = BYOM + one Copilot SDK sample; plugin/MCP packaging deferred |
| [0008](../adr/0008-cloud-escalation-policy.md) | Default-deny for cloud providers; per-client auth-bound allow-list |

---

## 2. Context and goals

### 2.1 Product boundary

Blaze.LlmGateway is a **single primary API host** that performs inference routing, tool orchestration, agent execution, and northbound API exposure. It is **not** a model-fine-tuning platform, a RAG pipeline, a generic HTTP API gateway, or a modalities-other-than-chat surface (see [PRD §4.2](../../PRD/blaze-llmgateway-prd.md#42-non-goals)).

Co-hosting the four planes in one process is locked by [ADR-0001](../adr/0001-primary-host-boundary.md). Internal layering is enforced via .NET project boundaries plus a NetArchTest fixture that fails the build on cross-layer references.

### 2.2 FR / NFR traceability

| PRD item | Design coverage |
|---|---|
| FR-01-1 OpenAI Chat Completions | §7.1 + [ADR-0003](../adr/0003-northbound-api-surface.md) |
| FR-01-2 Route to one of N providers | §4.2 Provider registry |
| FR-01-3/4 Meta + keyword routing | §4.3 Routing engine |
| FR-01-5/6 Fallback chains per destination | §4.4 Resilience |
| FR-01-7 Capability-based routing | §4.3 + [ADR-0002](../adr/0002-provider-identity-model.md) |
| FR-01-8 `X-LlmGateway-Provider` override | §7.1 Headers |
| FR-01-9 Structured routing-decision logs | §8.2 Observability |
| FR-02-1..8 Circuit breaker, streaming failover | §4.4 Resilience |
| FR-03-1..7 MCP lifecycle, `HostedMcpServerTool`, dynamic registration, per-request filtering, health | §5 Tool plane |
| FR-04-1..4 Auth, JWT, per-key allowlist, quotas | §8.1 Security + follow-on Auth ADR (§12) |
| FR-05-1..6 OTel, per-request metrics, dashboard, structured logs, benchmarks | §8.2 Observability + §10 Test strategy |
| FR-06-1..4 Caching | §8.4 Caching (scoped; deferred per §12) |
| FR-07-1..3 Rate limits | §8.3 + follow-on ADR (§12) |
| FR-08-1..4 Session mgmt | §6.2 Session model + [ADR-0004](../adr/0004-session-state-persistence.md) |
| FR-09-1..10 Consumer Web UI (Blazor) | §7.4.1 Blazor consumer UI |
| FR-10-1..6 Admin API | §7.3 Admin API |
| FR-11-1..10 Agent Framework DevUI | §7.4.2 DevUI (Microsoft Agent Framework) |
| NFR-01 Performance <1ms routing | §4.4 + §10 Benchmarks |
| NFR-02 Reliability / non-fatal MCP startup | §5.1 + §4.4 |
| NFR-03 Secrets / HTTPS / auth-before-routing | §8.1 + §9 Deployment |
| NFR-04 OTel schema, zero warnings | §8.2 + existing `-warnaserror` build |
| NFR-05 95% coverage | §10 Test strategy |
| NFR-06 Extensibility | §4.1 Provider catalog, §3.2 Project map |
| NFR-07 Deployment targets | §9 Deployment |

### 2.3 Scope of this document

**In scope:**

- Component contracts, DTOs, persistence schemas, middleware pipeline, project map.
- Phase 1 implementation detail for the inference and tool planes.
- Phase 3 contracts for the agent plane and Phase 4 client compatibility.
- Deployment topology for LAN and Aspire-orchestrated development.

**Out of scope (tracked in §12 as follow-on work):**

- Web UI visual design and component breakdown.
- Detailed auth middleware implementation (follow-on ADR).
- Semantic cache implementation details.
- Multi-tenancy posture (PRD OQ-1).
- Production cloud target selection (PRD OQ-3).

---

## 3. Architecture overview

### 3.1 Four-plane model

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                            Integration plane                                 │
│  OpenAI /v1/chat/completions  │  Admin /admin/*  │  (future) /v1/responses   │
│  /agents/* (Phase 3)          │  Copilot SDK sample  │  A2A (deferred)       │
└────────────────────────┬─────────────────────────────────────────────────────┘
                         │ typed DTOs, SSE
┌────────────────────────▼─────────────────────────────────────────────────────┐
│                              Agent plane                                     │
│  IAgentAdapter (Local, FoundryHosted, External)                              │
│  Workflows (Sequential/Concurrent/GroupChat/Handoff/Magentic)                │
│  ISessionStore (SQLite/Postgres/Cosmos — ADR-0004)                           │
└────────────────────────┬─────────────────────────────────────────────────────┘
                         │ IChatClient, AITool, ChatMessage
┌────────────────────────▼─────────────────────────────────────────────────────┐
│                              Tool plane                                      │
│  McpConnectionManager  ·  HostedMcpServerTool mapping  ·  tool policy        │
└────────────────────────┬─────────────────────────────────────────────────────┘
                         │ ChatOptions.Tools
┌────────────────────────▼─────────────────────────────────────────────────────┐
│                           Inference plane                                    │
│  IProviderRegistry  ·  IRoutingStrategy  ·  Circuit breaker + failover       │
│  Keyed IChatClient per ProviderDescriptor                                    │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Allowed dependencies** (per [ADR-0001](../adr/0001-primary-host-boundary.md)):

```
Integrations ──► Agents ──► Infrastructure (Inference + Tool) ──► Persistence ──► Core
      │           │               │                                  │
      └───────────┴───────────────┴──────────────────────────────────┘
                                    Core (no external deps)
```

### 3.2 Project map (target state at end of Phase 3)

| Project | Plane(s) | Today | Target |
|---|---|---|---|
| `Blaze.LlmGateway.Core` | all | `RouteDestination` enum, `LlmGatewayOptions` | `ProviderDescriptor`, `ModelProfile`, `CapabilityMetadata`, `ClientIdentity`, `RoutingContext`, `AgentDescriptor` |
| `Blaze.LlmGateway.Persistence` *(new)* | Agent (+Inference for audit) | — | `GatewayDbContext`, `ISessionStore`, `SessionEntity`, migrations |
| `Blaze.LlmGateway.Infrastructure` | Inference + Tool | Keyed providers, `LlmRoutingChatClient`, `McpConnectionManager`, `McpToolDelegatingClient` | `IProviderRegistry`, `ProviderClientFactory`, `CircuitBreakerDelegatingChatClient`, `CloudEscalationDelegatingChatClient`, hardened MCP |
| `Blaze.LlmGateway.Agents` *(new)* | Agent | — | `IAgentAdapter`, `AgentFrameworkAdapter`, `FoundryHostedAgentAdapter`, workflow runner |
| `Blaze.LlmGateway.Integrations` *(new)* | Integration | — | OpenAI DTOs, `ChatCompletionsEndpoint`, admin endpoints, SDK sample hooks |
| `Blaze.LlmGateway.Api` | composition | Minimal `Program.cs`, manual JSON parsing | DI composition root only — every endpoint is an extension method in `Integrations`. Also mounts the **Agent Framework DevUI** (`MapDevUI()`, FR-11) when enabled — see §7.4.2 |
| `Blaze.LlmGateway.Web` | UI | Blazor scaffold | Consumer chat + admin UI (FR-09); design in §7.4.1 |
| `Blaze.LlmGateway.AppHost` | Aspire | Ollama container, Foundry Local, GitHub Models, secrets | + persistence volume, + optional LM Studio / llama.cpp containers |
| `Blaze.LlmGateway.ServiceDefaults` | cross-cutting | OTel, HTTP resilience, service discovery | + gateway-specific OTel schema enrichers (§8.2) |
| `Blaze.LlmGateway.Tests` | — | Unit tests for routing | + architecture-assertion fixture, integration tests, SSE contract tests |
| `Blaze.LlmGateway.Benchmarks` | — | Placeholder | Routing overhead, per-provider P50/P95/P99 |
| `samples/Blaze.LlmGateway.Samples.CopilotSdk` *(new)* | — | — | Copilot SDK reference app (ADR-0007) |

### 3.3 Middleware pipeline (target state)

```
POST /v1/chat/completions
    │
    ▼
AuthMiddleware                            ← establishes ClientIdentity
    ▼
RateLimitingMiddleware                    ← per-client token & request budgets
    ▼
[Typed DTO → MEAI translation]
    ▼
IChatClient (composition root):
    ▼
McpToolDelegatingClient                   ← injects HostedMcpServerTool
    ▼
ResponseCacheDelegatingChatClient         ← Phase 5 (exact-match / semantic)
    ▼
CloudEscalationDelegatingChatClient       ← enforces ADR-0008 per request
    ▼
CircuitBreakerDelegatingChatClient        ← per-provider breaker & fallback chain
    ▼
LlmRoutingChatClient                      ← resolves provider via IRoutingStrategy
    ▼
Keyed IChatClient (per ProviderDescriptor)
```

Every layer is a `DelegatingChatClient` ([CLAUDE.md](../../CLAUDE.md) rule). Tests construct each layer in isolation with a fake inner client.

---

## 4. Inference plane

### 4.1 Provider catalog model

Replaces `RouteDestination` + per-provider options classes. See [ADR-0002](../adr/0002-provider-identity-model.md) for the full decision.

**Core types** in `Blaze.LlmGateway.Core/Providers/`:

```csharp
public sealed record ProviderDescriptor(
    string Id,
    ProviderKind Kind,
    string Endpoint,
    string? ApiKey,
    IReadOnlyList<ModelProfile> Models,
    bool Enabled = true,
    ProviderLocality Locality = ProviderLocality.Local,
    IReadOnlyDictionary<string, string>? Headers = null);

public enum ProviderKind { Ollama, OpenAICompatible, AzureOpenAI, Gemini }
public enum ProviderLocality { Local, Lan, Cloud }

public sealed record ModelProfile(
    string Id,                         // "{providerId}/{modelName}"
    string ProviderId,
    string ModelName,
    CapabilityMetadata Capabilities,
    ModelPricing? Pricing = null);

public sealed record CapabilityMetadata(
    int ContextWindowTokens,
    bool SupportsStreaming = true,
    bool SupportsToolCalls = false,
    bool SupportsVision = false,
    bool SupportsEmbeddings = false,
    string? FamilyTag = null);

public sealed record ModelPricing(decimal InputCostPer1K, decimal OutputCostPer1K, string Currency = "USD");
```

**Configuration validation.** `IValidateOptions<LlmGatewayOptions>` in `Blaze.LlmGateway.Infrastructure/Configuration/LlmGatewayOptionsValidator.cs`:

- `Providers[].Id` — non-empty, unique, matches `/^[a-z0-9][a-z0-9\-]+$/`.
- `Providers[].Models[].Id` — starts with `{Providers[].Id}/`, unique across all providers.
- `Routing.RouterModelId`, `Routing.FallbackModelId` — resolve to existing enabled models.
- `Providers[]` with `Kind=AzureOpenAI` and null `ApiKey` — valid (DefaultAzureCredential).
- `Providers[]` with `Kind=OpenAICompatible` and `ApiKey` absent — defaults to `"notneeded"` with a warning.

### 4.2 Provider registry

```csharp
// Blaze.LlmGateway.Infrastructure/Providers/IProviderRegistry.cs
public interface IProviderRegistry
{
    IReadOnlyCollection<ProviderDescriptor> Providers { get; }
    IReadOnlyCollection<ModelProfile> Models { get; }
    ProviderDescriptor GetProvider(string providerId);
    ModelProfile GetModel(string modelId);
    IChatClient GetChatClient(string modelId);
}

// Blaze.LlmGateway.Infrastructure/Providers/IProviderClientFactory.cs
public interface IProviderClientFactory
{
    IChatClient Create(ProviderDescriptor descriptor, ModelProfile model);
}
```

`ProviderRegistry` caches built `IChatClient`s keyed by `ModelProfile.Id`. It reacts to `IOptionsMonitor<LlmGatewayOptions>.OnChange` to hot-reload providers (Phase 3; no-op in Phase 1).

`ProviderClientFactory` maps `ProviderKind` to SDK wiring — translating exactly the rules in [CLAUDE.md](../../CLAUDE.md):

| Kind | Build |
|---|---|
| `Ollama` | `new OllamaApiClient(new Uri(descriptor.Endpoint), model.ModelName).AsIChatClient()` |
| `OpenAICompatible` | `new OpenAIClient(new ApiKeyCredential(descriptor.ApiKey ?? "notneeded"), new OpenAIClientOptions { Endpoint = new Uri(descriptor.Endpoint) }).GetChatClient(model.ModelName).AsIChatClient()` |
| `AzureOpenAI` | `descriptor.ApiKey is null ? new AzureOpenAIClient(new Uri(descriptor.Endpoint), new DefaultAzureCredential()) : new AzureOpenAIClient(new Uri(descriptor.Endpoint), new AzureKeyCredential(descriptor.ApiKey))` then `.GetChatClient(model.ModelName).AsIChatClient()` |
| `Gemini` | `new Google.GenAI.Client(apiKey: descriptor.ApiKey).AsIChatClient(model.ModelName)` |

All clients are finalized with `.AsBuilder().UseFunctionInvocation().Build()` per existing convention in [InfrastructureServiceExtensions.cs:22-30](../../Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs).

**Migration path from today's enum.** In Phase 1 a `RouteDestinationAdapter` bridges the old `RouteDestination` enum to the new catalog by mapping enum values to default model IDs. Marked `[Obsolete]` and removed in the release that closes Phase 2.

### 4.3 Routing engine

```csharp
public interface IRoutingStrategy
{
    Task<string> ResolveAsync(
        IEnumerable<ChatMessage> messages,
        RoutingContext context,
        CancellationToken ct = default);
}

public sealed record RoutingContext(
    ClientIdentity Client,
    string? RequestedModelId,                       // X-LlmGateway-Model header
    IReadOnlyList<string>? RequiredCapabilities);   // e.g. ["SupportsToolCalls", "ContextWindowTokens>=32000"]
```

**Built-in strategies** (chain-of-responsibility order — each falls through to the next on "no decision"):

1. `ExplicitOverrideStrategy` — if `context.RequestedModelId` is set and allowed by policy, return it. (PRD FR-01-8.)
2. `CapabilityMatchStrategy` — return the cheapest model satisfying `RequiredCapabilities`. Cost derived from `ModelPricing` (cloud) or `Locality` ordering (local < lan < cloud) for self-hosted.
3. `OllamaMetaRoutingStrategy` — current meta-router, updated prompt (see ADR-0008) to prefer local. Returns a `ModelProfile.Id`.
4. `KeywordRoutingStrategy` — hardened to recognize provider IDs from config (not hardcoded strings). Fallback.
5. `DefaultModelStrategy` — returns `Routing.FallbackModelId`.

Strategies are registered ordered via `AddSingleton<IRoutingStrategy>` and composed by a `CompositeRoutingStrategy` which walks them in sequence.

**`LlmRoutingChatClient` refactor.** The current implementation in [LlmRoutingChatClient.cs](../../Blaze.LlmGateway.Infrastructure/LlmRoutingChatClient.cs) implements `IChatClient` directly — violates [CLAUDE.md](../../CLAUDE.md) rule (middleware must inherit `DelegatingChatClient`). Refactor:

```csharp
public sealed class LlmRoutingChatClient(
    IChatClient defaultInner,               // the client used when routing cannot pick (shouldn't happen post-DefaultModelStrategy)
    IProviderRegistry registry,
    IRoutingStrategy strategy,
    IClientIdentityAccessor clientIdentity,
    ILogger<LlmRoutingChatClient> logger)
    : DelegatingChatClient(defaultInner)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        var target = await ResolveAsync(messages, options, ct);
        return await target.GetResponseAsync(messages, options, ct);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(...)
    { /* same, streaming */ }

    private async Task<IChatClient> ResolveAsync(...)
    {
        var ctx = new RoutingContext(clientIdentity.Current, options?.ModelId, ExtractRequiredCapabilities(options));
        var modelId = await strategy.ResolveAsync(messages, ctx, ct);
        logger.LogInformation("Routing to {ModelId} (strategy={Strategy})", modelId, strategy.GetType().Name);
        return registry.GetChatClient(modelId);
    }
}
```

### 4.4 Resilience

Resolves PRD FR-02-1..8.

**Circuit breaker per provider.** A new `CircuitBreakerDelegatingChatClient` layer in the pipeline tracks each `ProviderDescriptor`'s health state:

```csharp
public enum CircuitState { Closed, Open, HalfOpen }

public sealed class ProviderHealth
{
    public string ProviderId { get; init; }
    public CircuitState State { get; private set; } = CircuitState.Closed;
    public int ConsecutiveFailures { get; private set; }
    public DateTimeOffset? OpenedAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
}
```

**Transitions.**

- `Closed → Open` — after `FailureThreshold` consecutive failures (default 5). `OpenedAt = now`.
- `Open → HalfOpen` — after `CooldownSeconds` (default 30). Next request is the probe.
- `HalfOpen → Closed` — probe succeeds.
- `HalfOpen → Open` — probe fails. Reset `OpenedAt`.

Thresholds configurable globally under `LlmGateway:Resilience:CircuitBreaker` and overridable per-provider via `ProviderDescriptor.Resilience` (extension field, Phase 1 not required).

**Failure classification** (what counts as a failure vs. a "skip this provider, try the next"):

| Condition | Effect |
|---|---|
| HTTP 429 / `RateLimitExceededException` | Skip + schedule retry after `Retry-After`; not counted as breaker failure. |
| HTTP 401/403 / `AuthenticationException` | Skip + log as config error; not counted (fixing it requires config, not waiting). |
| `TaskCanceledException` (client) | Do not skip — propagate. |
| `TaskCanceledException` (upstream timeout) | Skip + count. |
| `AuthorizationException` from ADR-0008 (cloud escalation blocked) | Skip, not counted — policy, not provider health. |
| Network / 5xx / protocol error | Skip + count. |
| Success | Reset `ConsecutiveFailures`, `LastSuccessAt = now`. |

**Fallback chains.** Every `ModelProfile` has an implicit chain built from:

1. The routed model itself.
2. `ProviderDescriptor.Models` peers (same provider, capability-compatible).
3. Global `Routing.FallbackChains[modelId]: string[]` from config (ordered list of `ModelProfile.Id`s).
4. `Routing.FallbackModelId` as the final step.

On chain exhaustion, the client sees an `AllProvidersFailedException` wrapping the per-provider errors (FR-02-8), which maps to HTTP 503 per ADR-0003.

**Streaming failover.** Two cases:

- **Pre-stream** (no tokens yielded) — transparent retry on the next chain member. (FR-02-6)
- **Mid-stream** (at least one `ChatResponseUpdate` already yielded) — emit a terminal SSE `data: {"error": {...}}\n\n` frame and close. Do not attempt to splice two partial streams together. (FR-02-7)

Implementation: the circuit breaker layer consumes the provider stream into a buffered first chunk before yielding downstream. If the first chunk fails, retry; if not, pass through and degrade to terminal-error behavior on downstream failure.

**Routing overhead budget.** NFR-01 requires <1ms routing overhead. The pipeline adds: JSON deserialize (cheap, single alloc), routing-strategy resolve (O(n) over providers for capability match — n ≤ 50 expected), circuit-breaker check (dictionary lookup), tool append (already present). Benchmark fixture in §10 asserts <1ms at P99 excluding provider call.

### 4.5 Local runtime coverage

Per [ADR-0005](../adr/0005-local-runtime-compatibility.md): LM Studio and llama.cpp are `ProviderKind.OpenAICompatible` catalog entries. No specialized adapter. Example configs are in the ADR.

**Gemma-4 family** treatment: each Gemma-4 variant is its own `ModelProfile` under whichever provider hosts it (`lmstudio-workstation/gemma-4-9b`, `ollama-lan/gemma-4-27b`, `foundry-local/gemma-4-2b-phi`). Routing by family tag becomes `RequiredCapabilities: ["FamilyTag=gemma-4"]` against the capability-match strategy.

### 4.6 End-to-end sequence (streaming Chat Completion, Phase 1 target)

```
Client ── POST /v1/chat/completions ─────►  ChatCompletionsEndpoint
                                               │ validate DTO
                                               │ translate to MEAI
                                               ▼
                                            IChatClient (root)
                                               │
                                               ▼
                                            McpToolDelegatingClient
                                               │ options.Tools += MCP tools
                                               ▼
                                            CloudEscalationDelegatingChatClient
                                               │ resolves route + policy
                                               ▼
                                            CircuitBreakerDelegatingChatClient
                                               │ consults health, walks fallback chain
                                               ▼
                                            LlmRoutingChatClient
                                               │ strategy.ResolveAsync → modelId
                                               │ registry.GetChatClient(modelId)
                                               ▼
                                            keyed IChatClient
                                               │ GetStreamingResponseAsync
                                               ▼
                                            stream of ChatResponseUpdate
                                               │
                                            (each layer may short-circuit on failure)
                                               │
                                               ▼
                                            ChatCompletionsEndpoint serializes SSE
Client ◄── data: {"choices":[{"delta":{"content":"..."}}]}\n\n
Client ◄── data: {"choices":[...],"finish_reason":"stop"}\n\n
Client ◄── data: [DONE]\n\n
```

---

## 5. Tool plane

### 5.1 MCP connection lifecycle

Current state: [McpConnectionManager](../../Blaze.LlmGateway.Infrastructure/McpConnectionManager.cs) connects at startup (Stdio or HTTP), caches tools via `ListToolsAsync`, exposes `GetAllTools()`. Gaps: no reconnect, no dynamic registration, startup failure is caught but tools are silently unavailable, double-registration pattern between `IHostedService` and the singleton accessor (see PRD §2.4 item 6).

**Target design.** Refactor into a two-type structure:

```csharp
public interface IMcpConnectionManager
{
    IReadOnlyCollection<McpConnection> Connections { get; }
    Task<McpConnection> RegisterAsync(McpConnectionConfig config, CancellationToken ct);
    Task DeregisterAsync(string id, CancellationToken ct);
    IEnumerable<AITool> GetAllTools(McpToolFilter? filter = null);
}

public sealed class McpConnection
{
    public string Id { get; init; }
    public McpConnectionConfig Config { get; init; }
    public McpConnectionState State { get; private set; }  // Disconnected | Connecting | Connected | Reconnecting | Failed
    public IReadOnlyList<AITool> Tools { get; private set; }
    public DateTimeOffset? LastConnectedAt { get; private set; }
}

public enum McpConnectionState { Disconnected, Connecting, Connected, Reconnecting, Failed }
```

**Connection lifecycle.**

- **Startup.** Read `LlmGateway:Mcp:Servers[]` and call `RegisterAsync` for each.
- **Registration.** Construct transport, open client, call `ListToolsAsync`, map tools to `HostedMcpServerTool` (see §5.2). Failure marks the connection `Failed` and schedules a backoff reconnect.
- **Reconnect.** Exponential backoff (1s, 2s, 4s, … capped at 60s). State moves to `Reconnecting`. Per-connection cancellation tokens so shutdown is clean.
- **Shutdown.** `DisposeAsync` each `McpClient`, state `Disconnected`.

**Resolves**: FR-03-1 (Stdio/HTTP transports — already working), FR-03-7 (health + reconnect), NFR-02 (non-fatal startup — already working, formalized).

### 5.2 Tool mapping

Current `McpToolDelegatingClient.AppendMcpTools` ([file](../../Blaze.LlmGateway.Infrastructure/McpToolDelegatingClient.cs)) appends raw `AITool` instances. FR-03-4 requires mapping to `HostedMcpServerTool`. Refactor:

```csharp
private static HostedMcpServerTool ToHostedTool(McpConnection connection, AITool tool) =>
    tool is AIFunction fn
        ? new HostedMcpServerTool(connection.Id, fn)
        : throw new InvalidOperationException($"MCP tool {tool.GetType().Name} cannot be hosted");

private ChatOptions AppendMcpTools(ChatOptions? options, McpToolFilter filter)
{
    options ??= new ChatOptions();
    var existing = options.Tools is null ? [] : [.. options.Tools];
    foreach (var connection in mcpManager.Connections)
    {
        if (connection.State != McpConnectionState.Connected) continue;
        if (!filter.Allows(connection.Id)) continue;
        foreach (var tool in connection.Tools)
        {
            if (!filter.Allows(connection.Id, (tool as AIFunction)?.Name)) continue;
            existing.Add(ToHostedTool(connection, tool));
        }
    }
    options.Tools = existing;
    return options;
}
```

The existing `FunctionInvokingChatClient` built per-provider continues to execute tool calls via MEAI — no custom tool-calling loop, per [CLAUDE.md](../../CLAUDE.md) rule 2.

### 5.3 Tool policy and filtering

Per-provider / per-model / per-client allow-lists. Configuration:

```json
{
  "LlmGateway": {
    "Mcp": {
      "Servers": [
        { "Id": "microsoft-learn", "TransportType": "Stdio", "Command": "npx", "Arguments": ["-y", "@microsoft/mcp-server-microsoft-learn"] }
      ],
      "Policy": {
        "DefaultAllow": true,
        "PerClient": {
          "copilot-cli-internal": { "AllowedServers": ["microsoft-learn"] },
          "claude-code-internal": { "AllowedServers": ["microsoft-learn"], "DeniedTools": ["delete_*"] }
        },
        "PerModel": {
          "ollama-lan/router": { "AllowedServers": [] }
        }
      }
    }
  }
}
```

`McpToolFilter` is constructed per-request from `(ClientIdentity, ModelProfile.Id)` and consulted in `AppendMcpTools`. Resolves FR-03-6.

### 5.4 Admin API

Phase-1 endpoint set:

- `GET /admin/mcp` — returns `[{ id, state, transport, toolCount, lastConnectedAt }]`. Resolves FR-10-5.
- `POST /admin/mcp` — body is `McpConnectionConfig`; calls `IMcpConnectionManager.RegisterAsync`. Resolves FR-10-6 and FR-03-5.
- `DELETE /admin/mcp/{id}` — calls `DeregisterAsync`.

All admin endpoints require an admin-scoped API key (see §8.1, defined fully in the auth follow-on ADR).

---

## 6. Agent plane

### 6.1 Microsoft Agent Framework integration

Per [ADR-0006](../adr/0006-azure-foundry-agents-integration.md), all agents sit behind `IAgentAdapter`. The local-execution adapter is the common case.

```csharp
// Blaze.LlmGateway.Agents/Adapters/AgentFrameworkAdapter.cs
public sealed class AgentFrameworkAdapter(
    AgentDescriptor descriptor,
    IProviderRegistry registry,
    IMcpConnectionManager mcp,
    ISessionStore sessions,
    ILogger<AgentFrameworkAdapter> logger) : IAgentAdapter
{
    public string AgentId => descriptor.AgentId;
    public AgentKind Kind => AgentKind.Local;
    public AgentDescriptor Descriptor => descriptor;

    public async IAsyncEnumerable<AgentEvent> RunAsync(AgentRunRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatClient = registry.GetChatClient(descriptor.Metadata["ModelId"]);
        var agent = new ChatClientAgent(chatClient, options: new()
        {
            Name = descriptor.DisplayName,
            Instructions = descriptor.Metadata.GetValueOrDefault("Instructions"),
            Tools = ResolveTools(mcp, descriptor.SupportedToolNamespaces)
        });

        var history = await sessions.GetMessagesAsync(request.SessionId, maxTurns: null, ct);
        var conversation = new List<ChatMessage>(history);
        conversation.AddRange(request.NewMessages);

        await foreach (var update in agent.RunStreamingAsync(conversation, ct))
        {
            // fan-out ChatResponseUpdate into AgentEvent discriminated union
            foreach (var evt in TranslateToAgentEvents(update)) yield return evt;
            if (update.FinishReason is not null)
                await PersistTurnAsync(request.SessionId, update, ct);
        }
    }
}
```

### 6.2 Session model

Durable persisted sessions are Phase 1. See [ADR-0004](../adr/0004-session-state-persistence.md) for schema and persistence decisions.

**Session identity.**

- `sess_<ULID>` — user-level chat sessions.
- `dafx_<ULID>` — Durable Agent Framework runs (aligns with Agent Framework sample naming). The prefix distinguishes run state from user chat history for retention policies.

**Turn lifecycle.**

1. Client issues a Chat Completions request with `X-LlmGateway-Session-Id: sess_abc...`.
2. Integration plane calls `ISessionStore.GetMessagesAsync` and prepends stored history to `ChatCompletionRequest.Messages`.
3. Routing + inference executes.
4. On completion, integration plane calls `ISessionStore.AppendMessageAsync` for user + assistant messages, plus `RecordToolInvocationAsync` for any tool calls.

**Context-window truncation** (FR-08-3). When assembled history exceeds the target model's `ContextWindowTokens`, truncate oldest non-system turns first, keeping a running summary `session_message.seq=-1` system message. Summary regeneration triggered when truncation occurs; implementation deferred to Phase 3 but the schema supports it today.

### 6.3 Workflow orchestration

Phase 3 scope (not Phase 1). Supported patterns from `research/https-github-com-microsoft-agent-framework-samples.md`:

| Pattern | Phase | Notes |
|---|---|---|
| Sequential | 3 | Single-agent multi-step |
| Concurrent | 3 | Parallel agent fanout with aggregation |
| Handoff | 3 | Agent-to-agent delegation (routing decision becomes `HandoffTo(agentId)`) |
| GroupChat | 4 | Structured multi-party |
| Magentic | 4+ | Planning + progress-ledger orchestrator |

Workflow DSL lives in `Blaze.LlmGateway.Agents/Workflows/`. Executors reference agent IDs from `IAgentRegistry`; edges are declarative.

### 6.4 Agent adapters

- `AgentFrameworkAdapter` — §6.1 above. Local execution, gateway is source of truth.
- `FoundryHostedAgentAdapter` — per [ADR-0006](../adr/0006-azure-foundry-agents-integration.md). Delegates to Azure; gateway caches a read-only copy.

Adapter registry:

```csharp
// Blaze.LlmGateway.Agents/IAgentRegistry.cs
public interface IAgentRegistry
{
    IReadOnlyCollection<AgentDescriptor> List();
    IAgentAdapter Resolve(string agentId);
}
```

Registry is built from `LlmGateway:Agents[]` config at startup with one `IAgentAdapter` per descriptor.

---

## 7. Integration plane

### 7.1 OpenAI Chat Completions API

Single endpoint for Phase 1. Full DTO and endpoint detail in [ADR-0003](../adr/0003-northbound-api-surface.md).

**Endpoint file.** `Blaze.LlmGateway.Integrations/OpenAI/ChatCompletionsEndpoint.cs`:

```csharp
public static IEndpointRouteBuilder MapOpenAIChatCompletions(this IEndpointRouteBuilder routes)
{
    routes.MapPost("/v1/chat/completions", ChatCompletionsHandler.HandleAsync)
          .WithName("CreateChatCompletion")
          .Produces<ChatCompletionResponse>(StatusCodes.Status200OK)
          .WithOpenApi();
    return routes;
}
```

**Replacement for current Program.cs.** [Blaze.LlmGateway.Api/Program.cs](../../Blaze.LlmGateway.Api/Program.cs) shrinks to a composition root:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                              // OTel, resilience, discovery
builder.Services.AddGatewayPersistence(builder.Configuration);
builder.Services.AddGatewayInfrastructure(builder.Configuration);  // replaces AddLlmProviders + AddLlmInfrastructure
builder.Services.AddGatewayAgents(builder.Configuration);  // Phase 3
builder.Services.AddGatewayIntegrations();                 // endpoint wiring helpers

var app = builder.Build();
app.MapDefaultEndpoints();                                 // /health, /alive
app.MapOpenAIChatCompletions();
app.MapAdminEndpoints();
// app.MapAgentEndpoints();                                // Phase 3

app.Run();
```

**Headers** (summary; full table in ADR-0003):

| Header | Purpose | Phase |
|---|---|---|
| `Authorization: Bearer <key>` | Auth | 1 (minimal), 5 (full) |
| `X-LlmGateway-Model` | Override routing decision | 1 |
| `X-LlmGateway-Session-Id` | Session correlation | 1 (record), 3 (rehydrate) |
| `X-LlmGateway-NoCache` | Bypass cache | 5 |
| `X-LlmGateway-Client` | Client identity (alternative to bearer) | 5 |

### 7.2 Client compatibility matrix

| Client | Endpoint | Required features | Phase 1 status |
|---|---|---|---|
| OpenAI Python SDK | `/v1/chat/completions` | Spec-compliant streaming, `usage` in final chunk | Supported |
| OpenAI .NET SDK (`OpenAIClient`) | `/v1/chat/completions` | Same | Supported |
| GitHub Copilot CLI (BYOM) | `/v1/chat/completions` | `model` echo, streaming, `finish_reason=stop` | Supported |
| Claude Code (custom baseURL) | `/v1/chat/completions` | Streaming, tool_calls round-trip | Supported |
| Codex / OpenCode | `/v1/chat/completions` | Same | Supported |
| Copilot SDK sample | `/v1/chat/completions` + MCP | MCP tool round-trip via `HostedMcpServerTool` | Sample project under `samples/` |
| Internal Blazor Web UI (`Blaze.LlmGateway.Web`) | `/v1/chat/completions` via Aspire service discovery; `/admin/*` for ops pages; `/agents/*` (Phase 3) for agent picker | Streaming + session header + auth scope | Endpoint ready Phase 1; UI build-out Phase 4 (FR-09) |
| Agent Framework DevUI (in-process middleware) | `/devui` mounted in `Blaze.LlmGateway.Api`; reads OTel + `IAgentAdapter` registry | `MapDevUI()`; auth scope `devui`; cloud-policy enforced | Lands Phase 3 with the agent plane (FR-11; §7.4.2) |
| Microsoft Agent Framework hosts | `/v1/chat/completions` (via local `IChatClient` bridge) or `/agents/*` (Phase 3) | — | Chat Completions path supported; `/agents/*` deferred |
| Azure Foundry agents (hosted) | `/agents/*` (Phase 3) | `AgentEvent` stream | Deferred to Phase 3 |

Known caveats are documented inline in a `## Compatibility notes` subsection of each client doc under `docs/clients/` (populated as caveats surface during BYOM smoke tests).

### 7.3 Admin API (Phase 1–2)

`Blaze.LlmGateway.Integrations/Admin/AdminEndpoints.cs`:

```
GET    /admin/providers                    → list ProviderDescriptor + health
PATCH  /admin/providers/{id}               → enable/disable (FR-10-2)
GET    /admin/routing                      → current strategy chain + fallback config (FR-10-3)
PUT    /admin/routing                      → update strategies without restart (FR-10-4; Phase 2)
GET    /admin/mcp                          → list McpConnection (FR-10-5)
POST   /admin/mcp                          → register new MCP server (FR-10-6)
DELETE /admin/mcp/{id}                     → deregister
GET    /admin/sessions                     → list session summaries for a client
DELETE /admin/sessions/{id}                → purge a session (privacy / admin)
```

All admin endpoints sit behind an admin-scoped API key (follow-on Auth ADR).

### 7.4 UI surfaces

Blaze ships **three distinct UI surfaces**. They are orthogonal: each has its own audience, lifecycle, and threat model, and none is a substitute for the others. The matrix:

| Surface | Project / mount | Audience | Lifecycle | PRD ref |
|---|---|---|---|---|
| Aspire Dashboard | `Blaze.LlmGateway.AppHost` (auto via Aspire) | Any developer running the Aspire host locally | Always-on in dev; never shipped to production hosts | FR-05-5 |
| Blazor consumer UI | `Blaze.LlmGateway.Web` | End users + operators (chat, session browser, admin pages) | Built and deployed alongside the Api host | FR-09 |
| Microsoft Agent Framework DevUI | `Blaze.LlmGateway.Api` via `app.MapDevUI()` | Agent authors / developers debugging agent runs and workflow graphs | Default-enabled in `Development` environment; gated off in production | FR-11 |

#### 7.4.1 Blazor consumer UI

[Blaze.LlmGateway.Web](../../Blaze.LlmGateway.Web) is a Blazor Server app with a Syncfusion shell. The UI contract:

- **Chat page** — consumes `/v1/chat/completions` with `X-LlmGateway-Session-Id`. Streaming SSE rendered token-by-token. Provider override via `X-LlmGateway-Provider` exposed as a dropdown.
- **Session browser** — consumes `/admin/sessions` (list) and rehydrates a chosen session into the chat page.
- **Provider / MCP admin pages** — consume `/admin/providers`, `/admin/mcp`. Health badges fed by the `blaze.gateway.requests` and `blaze.gateway.errors` metrics from §8.2.
- **Agent picker + run console** (Phase 3+) — lists `IAgentAdapter`s from `IAgentRegistry` (ADR-0006), starts runs, streams `AgentEvent`s.
- **Service-discovery wiring** — the Web project reaches the Api via Aspire service discovery (`https+http://api`); no hardcoded URLs.
- **Auth** — same scheme as the `/v1` endpoint (FR-04 / §8.1). Anonymous chat is disabled when `LlmGateway:Auth:Required=true`.

Component-level UI design (Syncfusion grid/chat layout, theming) is deferred to a separate Web-UI design note (deferred D14). The backend contracts in §4–§7 are sufficient to begin the UI work in parallel.

#### 7.4.2 Microsoft Agent Framework DevUI

The **Microsoft Agent Framework DevUI** is an ASP.NET Core middleware shipped by Microsoft as part of the agent-framework family. It is **not** a part of .NET Aspire and **not** the Aspire Dashboard — those are separate, lower-level platform telemetry tools. DevUI's job is interactive agent + workflow debugging on top of the OTel trace stream the agent plane already emits (§8.2).

**Mount point.** A single line in `Program.cs`:

```csharp
// Blaze.LlmGateway.Api/Program.cs (Phase 3+)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("LlmGateway:DevUi:Enabled"))
{
    app.MapDevUI("/devui");   // discovers IAgentAdapter set + OTel feed automatically
}
```

**Discovery.** DevUI enumerates registered Agent Framework agents through the standard `AddAsAIAgent()` registration. Because `AgentFrameworkAdapter` (ADR-0006) is itself a thin wrapper over `ChatClientAgent`, every local agent shows up automatically. Foundry hosted agents (`FoundryHostedAgentAdapter`) are surfaced via a small `IAgentAdapter → IAIAgent` shim registered in `AddGatewayAgents`.

**Trace ingestion.** DevUI reads the same OTel spans defined in §8.2 (`agent.run`, `agent.tool_call`). One trace ledger, two consumers — the Aspire Dashboard for cross-resource platform view, DevUI for agent-centric drill-in.

**Gating policy.** Three layers, in order:

1. **Default-off outside dev.** `app.MapDevUI()` is only called when `IsDevelopment()` or `LlmGateway:DevUi:Enabled=true`.
2. **Auth scope.** When mounted, DevUI sits *inside* the auth pipeline. Callers without the `devui` scope on their `ClientIdentity` get 404 (not 401 — we do not confirm the surface exists).
3. **Cloud-escalation policy.** Agent runs initiated from DevUI flow through the same `CloudEscalationDelegatingChatClient` (ADR-0008). DevUI does not bypass policy; it *exercises* it.

**Secret hygiene.** Provider API keys, client API keys, Azure credentials, and OAuth tokens are redacted from any rendered trace payload (FR-11-10). The redaction list is the same one used by the OTel exporter in §8.2.

**Aspire Dashboard cross-link.** When mounted, DevUI's URL is registered as a resource link on the Api host in `Blaze.LlmGateway.AppHost` so it appears as a clickable target in the Aspire Dashboard:

```csharp
// Blaze.LlmGateway.AppHost/Program.cs (Phase 3+)
var api = builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
    .WithUrlForEndpoint("https", endpoint => endpoint.DisplayText = "Chat API")
    .WithUrl("/devui",     "Agent DevUI");
```

**Phasing.** DevUI lands with the rest of the agent plane in **Phase 3** (see §11). It is not a Phase-1 feature because there are no agents to debug until `IAgentAdapter` and `AgentFrameworkAdapter` exist (ADR-0006).

### 7.5 Deferred northbound surfaces

- `/v1/responses` (OpenAI Responses API) — durable-session native surface. Depends on ADR-0004 landing.
- `/agents/*` — Phase 3, specced in §6.
- A2A (Agent-to-Agent task protocol) — Phase 4+. Exposes `IAgentAdapter` as external HTTP.
- MCP export (`/mcp`) — Blaze as an MCP server. Deferred per ADR-0007 notes.
- `/v1/embeddings` — PRD non-goal today; flipped via a future ADR if required.

---

## 8. Cross-cutting concerns

### 8.1 Security and auth

Full auth middleware design is scheduled as a follow-on ADR (see §12). Phase 1 contract:

**Identity establishment.** `AuthMiddleware` runs before any pipeline middleware. It inspects `Authorization: Bearer <key>` (primary) or `X-LlmGateway-Client` (advanced), resolves it against `LlmGateway:Clients[]` config, and populates an `IClientIdentityAccessor` scoped service with the matching `ClientIdentity` record:

```csharp
public interface IClientIdentityAccessor
{
    ClientIdentity Current { get; }
}
```

**`ClientIdentity` fields** (consumed by the cloud-escalation layer from [ADR-0008](../adr/0008-cloud-escalation-policy.md)):

- `ClientId`, `DisplayName`
- `AllowedProviderIds: string[]`, optional `AllowedModelIds: string[]`
- `CloudPolicy: Denied | AllowListed | Unrestricted`
- `Quota: ClientQuota?` (per-minute/per-day token + request budgets, consumed by rate-limiting layer §8.3)
- `IsAdmin: bool` (admin API access)

**API key storage.** Keys are hashed (SHA-256 + per-client salt) before config-time persistence. Config entries store the hash; raw keys are only seen on request. Rotation via config reload.

**JWT (Phase 5 follow-on).** Structure planned: Entra-issued JWTs with `aud=blaze-llmgateway`, `scope=chat` or `scope=admin`. Design deferred to the Auth follow-on ADR.

**Non-LLM endpoints.** `/health`, `/alive` (from ServiceDefaults) remain unauthenticated. Every `/v1/*`, `/admin/*`, `/agents/*` endpoint requires auth — enforced at endpoint-routing level, not inside each handler.

### 8.2 Observability

**OpenTelemetry span schema.** Every chat completion emits one root span plus child spans per pipeline stage.

Root span — name: `llm.chat.completion`. Attributes:

| Attribute | Example | Source |
|---|---|---|
| `llm.request.id` | `req_01HZ...` (ULID) | Generated at endpoint entry |
| `llm.session.id` | `sess_01HZ...` | From `X-LlmGateway-Session-Id` header |
| `llm.client.id` | `copilot-cli-internal` | From `ClientIdentity` |
| `llm.provider.id` | `ollama-lan` | From final routed descriptor |
| `llm.model.id` | `ollama-lan/llama3.2` | From routed `ModelProfile.Id` |
| `llm.strategy` | `OllamaMetaRoutingStrategy` | Strategy that picked the model |
| `llm.fallback.count` | `0`..`N` | Number of chain hops |
| `llm.stream` | `true`/`false` | Request flag |
| `llm.input.tokens` | `245` | From provider response |
| `llm.output.tokens` | `187` | From provider response |
| `llm.latency.ms` | `1432` | End-to-end server time |
| `llm.escalation.allowed` | `true`/`false` | Cloud-escalation outcome |
| `llm.ttfb.ms` | `180` | Time-to-first-byte (streaming only) |

Child span names:

- `llm.routing.resolve` — with attribute `llm.routing.strategy`.
- `llm.circuitbreaker.check` — with attributes `llm.provider.state`, `llm.provider.consecutive_failures`.
- `llm.mcp.append_tools` — with attribute `llm.mcp.tool_count`.
- `llm.provider.call` — the MEAI `IChatClient` call itself; set attribute `llm.provider.sdk` (`OllamaApiClient`, `OpenAIClient`, etc.).

Agent-plane spans (Phase 3):

- `llm.agent.run` root. Children: `llm.agent.tool_call` per invocation, `llm.agent.step` per reasoning step.

**Metrics** (OTel, emitted via ServiceDefaults; names follow the OTel semantic conventions spirit):

| Metric | Unit | Dimensions |
|---|---|---|
| `blaze.gateway.requests` | count | `client.id`, `provider.id`, `model.id`, `status` |
| `blaze.gateway.latency` | histogram (ms) | same |
| `blaze.gateway.ttfb` | histogram (ms) | same |
| `blaze.gateway.tokens.input` | count | `client.id`, `provider.id`, `model.id` |
| `blaze.gateway.tokens.output` | count | same |
| `blaze.gateway.fallbacks` | count | `from.provider.id`, `to.provider.id` |
| `blaze.gateway.escalations.denied` | count | `client.id`, `provider.id` |
| `blaze.gateway.circuitbreaker.state` | gauge | `provider.id`, `state` |
| `blaze.gateway.mcp.tool.invocations` | count | `server.id`, `tool`, `status` |

**Structured logs.** Every log line carries `RequestId`, `SessionId`, `ClientId` in addition to its message fields. `FR-01-9` satisfied by the `llm.chat.completion` span + `blaze.gateway.requests` metric combo, plus an `Information` log per routing decision.

**Dashboards.** Two consumers of the same OTel ledger:

- **Aspire Dashboard** (FR-05-5) — auto-wired by `ServiceDefaults`. Cross-resource platform telemetry: traces, logs, metrics, resource graph. Always-on in dev. The canonical view for "is the host healthy."
- **Microsoft Agent Framework DevUI** (FR-11) — agent-centric trace drill-in, run replay, workflow graph rendering. Mounted in `Blaze.LlmGateway.Api` via `MapDevUI()`; gated per §7.4.2. Lands in Phase 3.

Phase 5 adds a dedicated provider-health page in the Blazor consumer UI (§7.4.1) consuming `/admin/providers` for operator-facing summaries that don't require opening the Aspire Dashboard.

### 8.3 Rate limiting and quotas

Per-client token-budget and request-rate limits (PRD FR-07). New pipeline middleware:

```csharp
public sealed class RateLimitingDelegatingChatClient(
    IChatClient inner,
    IClientIdentityAccessor clientIdentity,
    IRateLimitStore store,
    ILogger<RateLimitingDelegatingChatClient> logger) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(...)
    {
        var quota = clientIdentity.Current.Quota;
        if (quota is null) return base.GetResponseAsync(...);

        if (!await store.TryReserveAsync(clientIdentity.Current.ClientId, estimatedTokens: EstimateTokens(messages), ct))
            throw new RateLimitExceededException(retryAfter: store.GetRetryAfter(clientIdentity.Current.ClientId));

        var response = await base.GetResponseAsync(...);
        await store.RecordAsync(clientIdentity.Current.ClientId, response.Usage, ct);
        return response;
    }
}
```

`IRateLimitStore` implementations: `InMemoryRateLimitStore` (Phase 1 single-host), `RedisRateLimitStore` (Phase 5 multi-host).

### 8.4 Caching

Deferred to a follow-on ADR (§12). Design sketch:

- `ResponseCacheDelegatingChatClient` — layers in the pipeline above routing.
- **Exact-match** cache — key = `SHA256(model_id + normalized_messages + tool_set)`.
- **Semantic** cache — key-prefix + embedding similarity against recent entries; requires an embedding provider and a vector index. Implementation out of Phase-1 scope.
- Bypass via `X-LlmGateway-NoCache: true` (FR-06-4).

### 8.5 Error contract

Unified across all northbound surfaces. Shape per [ADR-0003](../adr/0003-northbound-api-surface.md):

```json
{ "error": { "message": "...", "type": "rate_limit_error", "param": null, "code": "rate_limit_exceeded" } }
```

`ExceptionToChatCompletionError` middleware (in `Blaze.LlmGateway.Integrations/OpenAI/`) maps known exceptions to the table in ADR-0003 §"Error handling". Any unmapped exception becomes `500 api_error internal_error` with a redacted message in production (original logged for operators).

### 8.6 Configuration validation on startup

All configuration binding happens through options validators that fail fast:

```csharp
// Blaze.LlmGateway.Api/Program.cs composition root
builder.Services.AddOptions<LlmGatewayOptions>()
    .Bind(builder.Configuration.GetSection(LlmGatewayOptions.SectionName))
    .ValidateOnStart()
    .Validate<IValidateOptions<LlmGatewayOptions>>();
```

Validator checks (non-exhaustive): every `Providers[].Id` unique and lowercased; every `Models[].Id` starts with its provider's Id; `Routing.RouterModelId` and `FallbackModelId` resolve; each `Clients[].ApiKeys[]` is non-empty; every `Agents[].ModelId` (Phase 3) resolves.

---

## 9. Deployment topology

### 9.1 LAN reference topology

```
                    ┌─────────────────────────┐
                    │    Clients on LAN       │
                    │  (Copilot CLI, Claude   │
                    │   Code, Blazor Web UI,  │
                    │   internal apps)        │
                    └────────────┬────────────┘
                                 │  HTTPS/HTTP
                                 ▼
      ┌──────────────────────────────────────────────────┐
      │  Blaze.LlmGateway.Api (single container)         │
      │  ┌─────────────────────────────────────────┐     │
      │  │ Integration plane (OpenAI + Admin API)  │     │
      │  │ Agent plane (Phase 3)                   │     │
      │  │ Tool plane (MCP)                        │     │
      │  │ Inference plane (routing + providers)   │     │
      │  └─────────────────────────────────────────┘     │
      │  Persistence: SQLite at /data/sessions.db        │
      └────┬────────────┬────────────┬───────────────────┘
           │            │            │
           ▼            ▼            ▼
  ┌──────────────┐ ┌─────────────┐ ┌───────────────────┐
  │ Ollama LAN   │ │ Foundry     │ │ Optional local    │
  │ 192.168.x.x  │ │ Local       │ │ LM Studio /       │
  │ (router +    │ │ (Phi-4, etc)│ │ llama.cpp         │
  │  llama3.2)   │ │             │ │                   │
  └──────────────┘ └─────────────┘ └───────────────────┘
           │
           └── optional egress (allow-listed) ──► cloud providers
                                                 (Azure Foundry,
                                                  GitHub Models,
                                                  Gemini, OpenRouter,
                                                  GitHub Copilot)
```

### 9.2 Aspire orchestration updates

[Blaze.LlmGateway.AppHost/Program.cs](../../Blaze.LlmGateway.AppHost/Program.cs) changes for the target topology:

1. **Persistence volume.** Aspire volume mount for `sessions.db`:
   ```csharp
   var api = builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
       .WithVolume("blaze-sessions", "/data");
   ```
   Add `LlmGateway__Persistence__ConnectionString=Data Source=/data/sessions.db` via `WithEnvironment`.

2. **Optional local runtimes.** LM Studio / llama.cpp as opt-in containers:
   ```csharp
   var lmStudio = builder.AddContainer("lmstudio", "lmstudio/server:latest")
       .WithEndpoint(port: 1234, targetPort: 1234, name: "api", scheme: "http")
       .WithVolume("lmstudio-models", "/models");
   ```
   Exposed to the API via reference and `LlmGateway__Providers__N__Endpoint` env var.

3. **Provider descriptor secrets.** Refactor the current `LlmGateway__Providers__AzureFoundry__ApiKey` env vars to the indexed form `LlmGateway__Providers__<N>__ApiKey` — or better, bind a `LlmGateway__ProviderSecrets__{providerId}__ApiKey` overlay that the validator merges into the array. The secret shape is orthogonal to the descriptor shape and can evolve independently.

4. **Agents and MCP config.** New `LlmGateway:Agents` (Phase 3) and `LlmGateway:Mcp:Servers` arrays bound from `appsettings.json` (non-secret) plus user-secrets for any API-key-gated MCP servers.

### 9.3 Container and cloud targets

| Target | Fit | Notes |
|---|---|---|
| **Local dev via Aspire** | Default | `dotnet run --project Blaze.LlmGateway.AppHost` |
| **Single-host LAN deployment** | Phase 1 primary target | `docker run` / `docker compose`; SQLite on a mounted volume |
| **Azure Container Apps** | Phase 5 cloud option | Matches Aspire deployment; Postgres via `ISessionStore` swap |
| **AKS / Kubernetes** | Phase 5+ | Same image; sidecars for Ollama/Foundry Local run as separate pods |

Final target selection is deferred to the Deployment follow-on ADR (§12).

### 9.4 Secret management

No change from current:

- **Dev.** `dotnet user-secrets` on the AppHost project. Aspire injects parameters as env vars ([CLAUDE.md §"Local Development Secrets"](../../CLAUDE.md)).
- **Prod.** Azure Key Vault references mapped onto the same parameter names; or environment variables in non-Azure environments.
- **Rotation.** Provider API keys rotate via config reload (no restart once `IOptionsMonitor` hot-reload lands in Phase 3). Client API keys rotate the same way.

---

## 10. Test strategy

### 10.1 Unit tests (target 95% line coverage — PRD NFR-05)

Expansion plan per plane. Table aligns with the existing [Blaze.LlmGateway.Tests](../../Blaze.LlmGateway.Tests) conventions (xUnit + Moq).

| Suite | Covers | Phase |
|---|---|---|
| `ProviderRegistryTests` | Descriptor binding, factory switch on `ProviderKind`, catalog validation | 1 |
| `LlmRoutingChatClientTests` (updated) | Streaming + non-streaming path, capability resolution, explicit override | 1 |
| `CompositeRoutingStrategyTests` | Strategy chain walking, fall-through | 1 |
| `CircuitBreakerDelegatingChatClientTests` | All state transitions (Closed↔Open↔HalfOpen), failure classification, fallback-chain walk, `AllProvidersFailedException` on exhaustion | 1 |
| `CloudEscalationDelegatingChatClientTests` | Denied/AllowListed/Unrestricted policies, skip vs. fail distinction | 1 |
| `McpToolDelegatingClientTests` | `HostedMcpServerTool` emission, filter honoring, empty-tool-set pass-through | 1 |
| `McpConnectionManagerTests` | Register/deregister, reconnect backoff, state transitions | 1 |
| `ChatCompletionsHandlerTests` | DTO binding, streaming SSE frame shape (`data: {...}\n\n`), `finish_reason` emission, error contract | 1 |
| `SessionStoreTests` | Append/get messages, tool-invocation round-trip, TTL cleanup, migration apply | 1 |
| `AgentFrameworkAdapterTests` | Run event translation, history rehydration, session persistence round-trip | 3 |
| `FoundryHostedAgentAdapterTests` | Event translation, Foundry-as-source-of-truth semantics | 3 |

**Architecture assertions.** New `ArchitectureTests.cs` using NetArchTest asserting:

- `Blaze.LlmGateway.Core` has no non-framework references.
- `Blaze.LlmGateway.Infrastructure` does not reference `Agents` or `Integrations`.
- `Blaze.LlmGateway.Agents` does not reference `Integrations`.
- No `IChatClient` implementation outside `Blaze.LlmGateway.Infrastructure` except `DelegatingChatClient` descendants. ([CLAUDE.md](../../CLAUDE.md) architectural rule: "New middleware must inherit from `DelegatingChatClient`".)

### 10.2 Integration tests

New `Blaze.LlmGateway.IntegrationTests` project using `WebApplicationFactory<Program>`:

- **SSE contract.** Start the host with a fake `IChatClient`; POST to `/v1/chat/completions` with `stream=true`; assert each frame is `data: {...}\n\n` and the last two are content-with-`finish_reason` and `data: [DONE]\n\n`.
- **Tool round-trip.** Register a stub MCP server exposing one tool; send a prompt that triggers tool calling; assert the session record captures request + response + latency.
- **Session round-trip.** Create a session, post three turns, restart the test host, re-post a turn with the same session id; assert the new turn sees all prior history.
- **Cloud escalation denial.** Configure a `Denied`-policy client; assert routing returns 403 when the meta-router picks a `Cloud` provider *and* all fallback-chain hops are also `Cloud`.
- **Circuit-breaker open/close.** Fake provider fails N times → breaker opens → next request skips it → after cooldown, half-open probe closes breaker on success.

### 10.3 Benchmarks (fills out the empty `Blaze.LlmGateway.Benchmarks` project — PRD FR-05-6)

BenchmarkDotNet suite targeting:

| Benchmark | Target | Guard |
|---|---|---|
| `RoutingOverhead.CompositeStrategy` | < 1 ms P99 (NFR-01) | Fail if P99 > 1.5 ms |
| `RoutingOverhead.CircuitBreakerCheck` | < 100 µs P99 | — |
| `ProviderCall.<Provider>.P50` | baseline | Regression alarm if > 2× baseline |
| `SessionStore.AppendMessage` | < 5 ms P99 on SQLite | Fail if > 20 ms |
| `McpToolAppend.N=10` | < 100 µs P99 | — |

Benchmarks run in CI on a dedicated runner (not on shared agents — noise ruins latency measurements).

### 10.4 Manual validation scripts

Under `samples/compatibility-tests/`:

- `copilot-cli-smoke.sh` — Copilot CLI BYOM happy path.
- `claude-code-smoke.sh` — Claude Code with custom `baseURL`.
- `openai-sdk-python.py` — stock `openai` SDK streaming + tool call.
- `load-test.sh` — k6 script running N concurrent streaming requests, asserts no goroutine/thread leaks.

These don't run in CI by default (require external providers), but are documented in `docs/clients/` and referenced by ADR-0007.

---

## 11. Phasing and migration roadmap

Maps the five phases from [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) onto design sections with the Phase-1 cut line explicit.

### Phase 1 — Gateway hardening (this design's primary deliverable)

**Inference plane (§4):**
- Introduce `ProviderDescriptor` / `ModelProfile` / `CapabilityMetadata` (§4.1, ADR-0002).
- Implement `ProviderRegistry` and `ProviderClientFactory` (§4.2).
- Refactor `LlmRoutingChatClient` to `DelegatingChatClient`, composable strategy chain (§4.3).
- Circuit breaker + fallback chains + pre-stream failover (§4.4).
- Keep old `RouteDestination` enum as `[Obsolete]` string-alias bridge; remove in Phase-2-closing release.

**Tool plane (§5):**
- `IMcpConnectionManager` refactor with reconnect and state machine (§5.1).
- `HostedMcpServerTool` mapping (§5.2).
- Per-client / per-model tool filter (§5.3).
- Admin endpoints for MCP lifecycle (§5.4).

**Integration plane (§7):**
- Typed OpenAI Chat Completions endpoint with SSE spec compliance (ADR-0003).
- Error-contract middleware (§8.5).
- Admin endpoints for providers + routing + MCP + sessions (§7.3).

**Agent plane precursor:**
- `Blaze.LlmGateway.Persistence` project with `ISessionStore` + SQLite migrations (ADR-0004).
- Session correlation in Chat Completions when `X-LlmGateway-Session-Id` is present (record only; rehydrate is Phase 3).
- Empty `IAgentAdapter` / `AgentDescriptor` contracts so Phase-3 consumers don't retrofit shapes.

**Cross-cutting:**
- Minimum-viable auth middleware with config-driven API keys → `ClientIdentity` (§8.1). Full Auth ADR still deferred.
- `CloudEscalationDelegatingChatClient` enforces default-deny (ADR-0008).
- OTel span schema rolled out across all pipeline layers (§8.2).

**Client ecosystem:**
- Copilot CLI BYOM docs + smoke test.
- Copilot SDK sample project (ADR-0007).

**Test and observability:**
- New integration-test project.
- Benchmarks project filled out; routing-overhead guard enforces NFR-01.
- NetArchTest architecture fixture enforces [ADR-0001](../adr/0001-primary-host-boundary.md) boundaries.

### Phase 2 — Provider/model catalog expansion

- LM Studio + llama.cpp catalog entries validated end-to-end (ADR-0005).
- Gemma-4 family models as capability-tagged profiles.
- Capability-based routing strategy ships (§4.3 item 2).
- Remove `[Obsolete]` `RouteDestination` enum.
- `IOptionsMonitor` hot-reload wiring.

### Phase 3 — Agent integration layer

- `Blaze.LlmGateway.Agents` project lands.
- `AgentFrameworkAdapter` + `FoundryHostedAgentAdapter` implement `IAgentAdapter` (ADR-0006).
- `/agents/*` northbound endpoints (still no Responses API yet).
- Session rehydration (not just recording) in Chat Completions.
- Workflow orchestration MVP: Sequential + Concurrent + Handoff.
- **Microsoft Agent Framework DevUI** mounted (`MapDevUI()`) — FR-11; default-on in Development, gated in production. Cross-linked from the Aspire Dashboard. See §7.4.2.

### Phase 4 — Client ecosystem polish

- **Blazor consumer UI** (FR-09 / §7.4.1) wired to Chat Completions, `/admin/*`, and `/agents/*` — chat page, session browser, provider/MCP admin pages, agent picker, auth.
- Responses API consideration — follow-on ADR if promoted.
- A2A evaluation — follow-on ADR if promoted.
- MCP packaging for Copilot evaluation — follow-on ADR.

### Phase 5 — Security, policy, operational readiness

- Full Auth ADR lands (JWT + Entra + rotation).
- Rate-limiting Redis store.
- Caching ADR + `ResponseCacheDelegatingChatClient`.
- Multi-tenancy ADR (PRD OQ-1).
- Deployment target ADR (PRD OQ-3) + production CD pipeline.
- Postgres / Cosmos DB session-store swap for multi-host deployments.

---

## 12. Deferred decisions / follow-on ADRs

These are **not resolved** by this document. They are listed with recommended sequencing so they can be opened at the right phase.

| # | Topic | Sources | Proposed phase |
|---|---|---|---|
| D1 | **Auth middleware detail** — JWT, Entra, API-key rotation, admin-scope model | PRD FR-04, §8.1 | Phase 1 minimum-viable, full ADR by end of Phase 1 |
| D2 | **Multi-tenancy posture** — per-tenant provider pools, quotas, isolation | PRD OQ-1 | Phase 5 |
| D3 | **Caching strategy** — core vs. pluggable, exact-match vs. semantic, backend | PRD OQ-2, FR-06, §8.4 | Phase 5 |
| D4 | **Deployment target** — ACA vs. AKS vs. self-hosted | PRD OQ-3, §9.3 | End of Phase 1 |
| D5 | **Admin API co-host vs. separate service** | PRD OQ-7 | Phase 4 (only if security posture demands separation) |
| D6 | **Client SDK vs. OpenAI-compat only** | PRD OQ-9 | Phase 4 |
| D7 | **Meta-router model deployment** — sidecar vs. separate service vs. cloud | PRD OQ-10 | Phase 2 |
| D8 | **Copilot plugin/MCP packaging** | planning draft Q3, ADR-0007 | Phase 4+ |
| D9 | **Responses API adoption** | ADR-0003, planning draft | Phase 4 |
| D10 | **A2A northbound surface** | ADR-0006 scope note | Phase 4+ |
| D11 | **MCP server export** (Blaze as MCP server) | §7.5 | Phase 4+ |
| D12 | **Streaming failover mid-stream splicing** | PRD OQ-8 | If/when a consumer demands it; today: terminal-error-frame semantics (§4.4) |
| D13 | **Cost-tracking source of truth** | PRD OQ-6 | Phase 5 |
| D14 | **Blazor consumer UI component design** — Syncfusion layout, theming, page breakdown | PRD FR-09, §7.4.1 | Phase 4, separate design note |
| D15 | **Observability schema freeze** | NFR-04, §8.2 | Phase 2 (after field use proves the attribute set) |
| D16 | **DevUI production-gating policy** — runtime flag vs. build-time trim, scope semantics, secret-redaction list, 404-vs-401 stance | PRD FR-11, OQ-11, §7.4.2 | Phase 3 (alongside `MapDevUI()` mount) |

Each follow-on ADR will be authored when its phase opens, using the [template](../adr/0000-adr-template.md).

---

## 13. ADR index

Ordered by number. Each file lives in [../adr/](../adr).

| # | Title | Status | One-line |
|---|---|---|---|
| [0001](../adr/0001-primary-host-boundary.md) | Primary host boundary | Proposed | Co-host gateway core + agent runtime in the Api project; enforce layering via project boundaries + NetArchTest. |
| [0002](../adr/0002-provider-identity-model.md) | Provider identity model | Proposed | Replace `RouteDestination` enum with config-driven `ProviderDescriptor` + `ModelProfile` + `CapabilityMetadata`. |
| [0003](../adr/0003-northbound-api-surface.md) | Northbound API surface | Proposed | Phase 1 ships typed OpenAI Chat Completions only; Responses/A2A deferred. |
| [0004](../adr/0004-session-state-persistence.md) | Session state persistence | Proposed | SQLite + EF Core `ISessionStore` abstraction with swap path to Postgres/Cosmos. |
| [0005](../adr/0005-local-runtime-compatibility.md) | Local runtime compatibility | Proposed | LM Studio + llama.cpp as `OpenAICompatible` catalog entries, no specialized adapter. |
| [0006](../adr/0006-azure-foundry-agents-integration.md) | Azure Foundry agents | Proposed | `IAgentAdapter` pattern fronts both local Agent Framework and Foundry hosted agents. |
| [0007](../adr/0007-copilot-ecosystem-strategy.md) | Copilot ecosystem strategy | Proposed | BYOM docs + one SDK sample in Phase 1; plugin/MCP packaging deferred. |
| [0008](../adr/0008-cloud-escalation-policy.md) | Cloud escalation policy | Proposed | Default-deny cloud; per-client allow-list enforced by a pipeline layer. |

---

## Document conventions

- **C# style:** primary constructors, collection expressions (`[]`), nullable reference types enabled, `CancellationToken` propagated. Matches [CLAUDE.md](../../CLAUDE.md).
- **Build:** `dotnet build --no-incremental -warnaserror` remains the gate. All code referenced in this document must pass.
- **Middleware rule:** every new pipeline layer inherits `DelegatingChatClient`. No raw `IChatClient` implementations outside the provider registry.
- **MEAI-is-the-law:** no raw `HttpClient` to LLM providers. Always `IChatClient` + `ChatMessage` + `ChatOptions`.
- **Links:** relative paths to repo files so the document renders correctly in IDEs and in GitHub previews.

## Changelog

| Date | Change |
|---|---|
| 2026-04-17 | Initial draft. All sections and ADRs 0001–0008 authored. Status: Proposed. |
| 2026-04-18 | UI surfaces revision: §7.4 expanded into three-surface model (Aspire Dashboard, Blazor consumer UI §7.4.1, Microsoft Agent Framework DevUI §7.4.2). FR-09 elevated; FR-11 added in PRD. Phase 3 picks up `MapDevUI()` mount; D16 added for the DevUI production-gating ADR. |

