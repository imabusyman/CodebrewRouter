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
| FR-09-1..7 Web UI | §7.4 (scope call-out; UI design out of scope for this doc) |
| FR-10-1..6 Admin API | §7.3 Admin API |
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
| `Blaze.LlmGateway.Api` | composition | Minimal `Program.cs`, manual JSON parsing | DI composition root only — every endpoint is an extension method in `Integrations` |
| `Blaze.LlmGateway.Web` | UI | Blazor scaffold | Chat UI + admin UI (deferred; see §7.4) |
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
| Internal Blazor Web UI | `/v1/chat/completions` via service discovery | Streaming + session header | Deferred UI, endpoint ready |
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

### 7.4 Web UI (scope call-out only)

[Blaze.LlmGateway.Web](../../Blaze.LlmGateway.Web) is a Blazor Server app today with only a Syncfusion shell. The UI contract is:

- **Chat surface** — consumes `/v1/chat/completions` with session header.
- **Provider/MCP dashboard** — consumes `/admin/*`.
- **Session browser** — consumes `/admin/sessions` and the chat surface.

Component-level UI design is deferred to a separate Web-UI design note. The backend contracts this doc specifies are sufficient to start the UI work in parallel.

### 7.5 Deferred northbound surfaces

- `/v1/responses` (OpenAI Responses API) — durable-session native surface. Depends on ADR-0004 landing.
- `/agents/*` — Phase 3, specced in §6.
- A2A (Agent-to-Agent task protocol) — Phase 4+. Exposes `IAgentAdapter` as external HTTP.
- MCP export (`/mcp`) — Blaze as an MCP server. Deferred per ADR-0007 notes.
- `/v1/embeddings` — PRD non-goal today; flipped via a future ADR if required.

---
