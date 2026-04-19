# PRD — Blaze.LlmGateway (CodebrewRouter)

> **Document type:** Product Requirements Document (Baseline)
> **Purpose:** Captures the current state of the system and defines a structured surface for future extension planning. No implementation tasks are included — this is a planning foundation only.

---

## 1. Product Vision

Blaze.LlmGateway is an **intelligent, agentic LLM routing proxy** — a single API surface that accepts OpenAI-compatible chat requests and transparently routes them to the best available LLM provider based on intent, context, availability, and cost. It operates as infrastructure middleware: clients talk to one endpoint, the gateway handles model selection, tool augmentation, resilience, and observability.

**North-star goal:** Become the single, self-healing LLM gateway for any .NET application or team — provider-agnostic, observable, extensible via MCP tools, and fully configurable without code changes.

---

## 2. Current State (v0 — Implemented)

### 2.1 What Exists Today

| Area | Status | Details |
|---|---|---|
| **API endpoint** | ✅ Working | `POST /v1/chat/completions` — OpenAI-compatible, SSE streaming |
| **9 providers** | ✅ Registered | AzureFoundry, Ollama, OllamaBackup, GithubCopilot, Gemini, OpenRouter, FoundryLocal, GithubModels, OllamaLocal |
| **MEAI pipeline** | ✅ Working | McpToolDelegatingClient → LlmRoutingChatClient → keyed provider |
| **Meta-routing** | ✅ Working | Ollama router model classifies last user message into a RouteDestination |
| **Keyword fallback routing** | ✅ Working | Regex/keyword scan; defaults to AzureFoundry |
| **MCP tool injection** | ✅ Partial | McpConnectionManager connects via Stdio/HTTP; AppendMcpTools injects tools into ChatOptions |
| **Aspire orchestration** | ✅ Working | AppHost provisions Ollama container, FoundryLocal, GitHub Models; injects secrets as env vars |
| **Aspire Dashboard** | ✅ Working | Platform-telemetry UI (traces, logs, metrics, resource graph) auto-provisioned by `Blaze.LlmGateway.AppHost` via `ServiceDefaults` |
| **Blazor Web UI** | 🟡 Scaffold | Syncfusion Blazor shell; slated as the consumer chat + admin surface; no API connection or chat component wired yet |
| **Microsoft Agent Framework DevUI** | ❌ Missing | Interactive agent / workflow dev surface (`MapDevUI()` middleware from the Agent Framework NuGet). Lands with the agent plane |
| **Unit tests** | 🟡 Minimal | 3 tests: LlmRoutingChatClient (2), OllamaMetaRoutingStrategy (1) |
| **Aspire-orchestrated integration tests** | ❌ Missing | `Aspire.Hosting.Testing` not referenced in `Blaze.LlmGateway.Tests`; no `DistributedApplicationTestingBuilder` harness exists. Required by NFR-05 (Tier B) and FR-11-11..14 |
| **In-process integration tests** (`WebApplicationFactory<Program>`) | ❌ Missing | No SSE-contract, MCP round-trip, or session round-trip tests today. Required by NFR-05 (Tier A) |
| **Api host wiring of `AddServiceDefaults()`** | ❌ Missing | `Blaze.LlmGateway.Api/Program.cs` references `ServiceDefaults` but never calls `builder.AddServiceDefaults()` or `app.MapDefaultEndpoints()`. Result: no OTel, resilience, service discovery, or `/health` `/alive` endpoints on the API host. The Web project is wired correctly |
| **Benchmarks** | 🟡 Scaffold | BenchmarkDotNet project exists; Program.cs is a placeholder |
| **Circuit breaker** | ❌ Missing | No provider health tracking |
| **Streaming failover** | ❌ Missing | Mid-stream failure handling not implemented |
| **Authentication** | ❌ Missing | No API key / bearer token auth on the gateway itself |
| **Rate limiting** | ❌ Missing | No per-client or per-provider throttle management |
| **Response caching** | ❌ Missing | No semantic or exact-match cache |
| **Conversation sessions** | ❌ Missing | No server-side session management |
| **Admin / config API** | ❌ Missing | No dynamic routing rules, no health dashboard |
| **Cost / token tracking** | ❌ Missing | No usage metering |

### 2.2 Current Architecture Diagram

```
Client (HTTP)
    │
    ▼
POST /v1/chat/completions  (Blaze.LlmGateway.Api)
    │
    ▼
McpToolDelegatingClient         ← DelegatingChatClient; injects AITools from McpConnectionManager
    │
    ▼
LlmRoutingChatClient            ← implements IChatClient (gap: should be DelegatingChatClient)
    │
    ├─► OllamaMetaRoutingStrategy  → asks keyed "Ollama" to classify RouteDestination
    │       └─ fallback: KeywordRoutingStrategy → default: AzureFoundry
    │
    ▼
IServiceProvider.GetKeyedService<IChatClient>(destination.ToString())
    │
    ├── "AzureFoundry"     AzureOpenAIClient + UseFunctionInvocation
    ├── "Ollama"           OllamaApiClient   + UseFunctionInvocation  ← also used as router
    ├── "OllamaBackup"     OllamaApiClient   + UseFunctionInvocation
    ├── "GithubCopilot"    OpenAIClient      + UseFunctionInvocation
    ├── "Gemini"           Google.GenAI      + UseFunctionInvocation
    ├── "OpenRouter"       OpenAIClient      + UseFunctionInvocation
    ├── "FoundryLocal"     AzureOpenAIClient + UseFunctionInvocation
    ├── "GithubModels"     OpenAIClient      + UseFunctionInvocation
    └── "OllamaLocal"      OllamaApiClient   + UseFunctionInvocation
```

### 2.3 Configuration System

- All provider config is bound from `LlmGateway:Providers:<Name>` JSON section into `LlmGatewayOptions`.
- `LlmGateway:Routing:RouterModel` (default: `"router"`) and `FallbackDestination` (default: `"AzureFoundry"`) are configurable.
- Secrets are managed via `dotnet user-secrets` on the **AppHost project only**, injected as env vars at runtime by Aspire.
- Production: same parameter names mapped as env vars or Azure Key Vault references.

### 2.4 Known Technical Debt

1. `LlmRoutingChatClient` implements `IChatClient` directly — must be refactored to `DelegatingChatClient`.
2. `McpToolDelegatingClient` does not map tools to `HostedMcpServerTool` — injects raw `AITool` objects.
3. No fallback chain — `LlmRoutingChatClient` only falls back to its `innerClient` (AzureFoundry) if the resolved key is missing.
4. Benchmarks project is a placeholder (`Console.WriteLine("Hello, World!")`).
5. `KeywordRoutingStrategy` does not recognize `FoundryLocal`, `GithubModels`, or `OllamaLocal` as keywords.
6. `McpConnectionManager` double-registration pattern (IHostedService + manually resolved singleton) is fragile.
7. `Blaze.LlmGateway.Api/Program.cs` references `ServiceDefaults` but never invokes `builder.AddServiceDefaults()` or `app.MapDefaultEndpoints()`. The API host therefore runs without OpenTelemetry, the standard HTTP resilience handler, service discovery, or `/health`/`/alive` endpoints — the very things `ServiceDefaults` exists to provide. The Web project (`Blaze.LlmGateway.Web/Program.cs:7`) is wired correctly. **Phase-1 fix.**
8. `Blaze.LlmGateway.Tests` has no Aspire integration-test harness. Adding `Aspire.Hosting.Testing` and a one-line `GET /alive` smoke test would have caught (7) automatically. **Phase-1 fix.**

---

## 3. Problem Statement

### 3.1 User Problems
- **Fragmentation:** Developers maintain per-app provider integrations with duplicated auth, retry, and error handling logic.
- **No intelligent selection:** Every app is hard-coded to a single model; switching requires code changes.
- **Observability gap:** Token usage, cost, and latency across providers is invisible.
- **Resilience is DIY:** Rate limits and provider outages cause silent failures with no failover.
- **No tooling surface:** MCP tools and function calling are configured per-application, not shared.

### 3.2 Operator Problems
- No way to enforce policy (which users can use which models, spending caps).
- No visibility into aggregate usage across clients.
- No way to add or remove providers or routing rules without code deployments.

---

## 4. Goals and Non-Goals

### 4.1 Goals (Scope for Future Versions)

| Priority | Goal |
|---|---|
| P0 | Resilient routing: circuit breaker, configurable fallback chains, streaming failover |
| P0 | Full MCP tool pipeline: HostedMcpServerTool mapping, dynamic MCP server registration |
| P0 | Consumer **Blazor chat UI**: functional chat interface wired to `/v1/chat/completions` with streaming, provider override, and session switcher |
| P1 | Gateway authentication: API key / bearer token auth protecting the `/v1` endpoint |
| P1 | Full test coverage: 95% line coverage, all failure scenarios covered |
| P1 | **Microsoft Agent Framework DevUI** surface (`MapDevUI()`): interactive agent + workflow dev/debug UI, mounted inside the Api host, gated to non-production by default |
| P1 | Provider health dashboard: per-provider status, latency, error rate (page inside the Blazor UI) |
| P2 | Conversation session management: server-side multi-turn history |
| P2 | Token usage and cost tracking: per-request, per-client, per-provider |
| P2 | Response caching: exact-match and semantic cache layer |
| P2 | Rate limiting: per-client and per-provider throttle enforcement |
| P3 | Admin API: runtime routing rule changes, provider enable/disable |
| P3 | Multi-tenancy: per-tenant provider pools, quotas, routing policies |
| P3 | Model capability routing: route by context window, vision, embedding support |

### 4.2 Non-Goals
- This is not a fine-tuning or training platform.
- This is not a vector database or RAG pipeline (though it can call MCP tools that provide retrieval).
- This is not a general-purpose API gateway (no HTTP proxy, no auth for non-LLM routes).
- No support for non-chat modalities (images, audio, embeddings) unless explicitly added later.

---

## 5. Functional Requirements

### FR-01 — Core Routing

| ID | Requirement | Current State |
|---|---|---|
| FR-01-1 | Accept OpenAI-compatible `POST /v1/chat/completions` with streaming SSE response | ✅ Done |
| FR-01-2 | Route to one of 9 providers based on IRoutingStrategy | ✅ Done |
| FR-01-3 | Meta-routing via Ollama classifier | ✅ Done |
| FR-01-4 | Keyword fallback routing | ✅ Done |
| FR-01-5 | Configurable fallback destination via `RoutingOptions.FallbackDestination` | ✅ Done (config only) |
| FR-01-6 | Configurable fallback chain per RouteDestination (ordered list of alternatives) | ❌ Not implemented |
| FR-01-7 | Route by model capability (e.g., prefer provider with largest context window) | ❌ Not implemented |
| FR-01-8 | Explicit provider override via `X-LlmGateway-Provider` request header | ❌ Not implemented |
| FR-01-9 | Routing decision logged with structured fields: provider, latency, strategy used | 🟡 Partial (provider name logged) |

### FR-02 — Resilience

| ID | Requirement | Current State |
|---|---|---|
| FR-02-1 | Per-provider circuit breaker: closed → open → half-open state machine | ❌ Not implemented |
| FR-02-2 | Configurable failure threshold and cooldown period per provider | ❌ Not implemented |
| FR-02-3 | Rate-limit detection (HTTP 429 / `Retry-After`) → skip to next chain member | ❌ Not implemented |
| FR-02-4 | Timeout detection (`TaskCanceledException`) → skip to next chain member | ❌ Not implemented |
| FR-02-5 | Auth failure (HTTP 401/403) → skip, log as config error, do not retry | ❌ Not implemented |
| FR-02-6 | Streaming failover: if no tokens yielded yet, retry on next provider transparently | ❌ Not implemented |
| FR-02-7 | Streaming failover: if tokens already yielded, emit error SSE frame and close | ❌ Not implemented |
| FR-02-8 | AggregateException on chain exhaustion wrapping all per-provider errors | ❌ Not implemented |

### FR-03 — MCP Tool Integration

| ID | Requirement | Current State |
|---|---|---|
| FR-03-1 | Connect to MCP servers via Stdio and HTTP transports at startup | ✅ Done |
| FR-03-2 | Cache tool list per MCP server | ✅ Done |
| FR-03-3 | Inject all available tools into ChatOptions.Tools on each request | ✅ Partial (raw AITool, not HostedMcpServerTool) |
| FR-03-4 | Map MCP tool definitions to HostedMcpServerTool for proper MEAI function invocation | ❌ Not implemented |
| FR-03-5 | Support dynamic MCP server registration at runtime (add/remove servers without restart) | ❌ Not implemented |
| FR-03-6 | Per-request tool filtering (allow clients to specify which MCP servers/tools to include) | ❌ Not implemented |
| FR-03-7 | MCP server health monitoring — reconnect on failure | ❌ Not implemented |

### FR-04 — Authentication and Authorization

| ID | Requirement | Current State |
|---|---|---|
| FR-04-1 | API key authentication on all `/v1` endpoints | ❌ Not implemented |
| FR-04-2 | Bearer token (JWT) support as alternative to API key | ❌ Not implemented |
| FR-04-3 | Per-key provider allowlist (key A may only use AzureFoundry, key B can use all) | ❌ Not implemented |
| FR-04-4 | Per-key quota enforcement (max tokens/day, max requests/minute) | ❌ Not implemented |

### FR-05 — Observability and Monitoring

| ID | Requirement | Current State |
|---|---|---|
| FR-05-1 | OpenTelemetry traces on all LLM calls (via ServiceDefaults) | 🟡 Partial (Aspire OTel wired, LLM spans not emitted) |
| FR-05-2 | Per-request metrics: provider, latency (ms), input tokens, output tokens, total cost | ❌ Not implemented |
| FR-05-3 | Provider health dashboard in the Web UI | ❌ Not implemented |
| FR-05-4 | Structured log at every routing decision with: strategy, destination, fallback used, duration | 🟡 Partial |
| FR-05-5 | **Aspire Dashboard** integration for live request traces, resource health, and structured logs — auto-wired by `ServiceDefaults`; canonical platform-telemetry surface (not to be confused with Agent Framework DevUI, FR-11) | ✅ Wired via ServiceDefaults; LLM-specific span attributes still pending (FR-05-1/2) |
| FR-05-6 | BenchmarkDotNet suite: P50/P95/P99 latency per provider, routing overhead < 1 ms | ❌ Placeholder only |

### FR-06 — Response Caching

| ID | Requirement | Current State |
|---|---|---|
| FR-06-1 | Exact-match cache: identical message list returns cached response, bypassing provider call | ❌ Not implemented |
| FR-06-2 | Semantic cache: use embedding similarity to return cached response for near-duplicate prompts | ❌ Not implemented |
| FR-06-3 | Cache TTL configurable per provider or globally | ❌ Not implemented |
| FR-06-4 | Cache bypass header: `X-LlmGateway-NoCache: true` | ❌ Not implemented |

### FR-07 — Rate Limiting

| ID | Requirement | Current State |
|---|---|---|
| FR-07-1 | Per-client token-budget rate limiting (tokens/minute, tokens/day) | ❌ Not implemented |
| FR-07-2 | Per-provider request-rate limiting (requests/second enforced client-side before calling provider) | ❌ Not implemented |
| FR-07-3 | Configurable limit overrides per API key / tenant | ❌ Not implemented |

### FR-08 — Conversation Session Management

| ID | Requirement | Current State |
|---|---|---|
| FR-08-1 | Server-side session: persist message history keyed by session ID | ❌ Not implemented |
| FR-08-2 | Session ID passed via `X-LlmGateway-Session-Id` header or request body | ❌ Not implemented |
| FR-08-3 | Automatic context window truncation when history exceeds provider limit | ❌ Not implemented |
| FR-08-4 | Session expiry / TTL | ❌ Not implemented |

### FR-09 — Consumer Web UI (Blazor)

The gateway ships **three distinct UI surfaces**, each with a separate role. FR-09 covers the consumer chat + admin surface; FR-11 covers the Agent Framework DevUI; the Aspire Dashboard (already live via `ServiceDefaults`) covers platform telemetry.

| Surface | Project / host | Role | Audience |
|---|---|---|---|
| **Aspire Dashboard** | `Blaze.LlmGateway.AppHost` | Traces, logs, metrics, resource graph | Any developer running Aspire locally |
| **Blazor consumer UI** (FR-09) | `Blaze.LlmGateway.Web` | Chat, provider override, session history, provider/MCP admin pages | End users + operators |
| **Agent Framework DevUI** (FR-11) | Mounted in `Blaze.LlmGateway.Api` via `MapDevUI()` | Interactive agent/workflow debugging, trace replay | Agent authors, developers |

| ID | Requirement | Current State |
|---|---|---|
| FR-09-1 | Chat page with message input and streaming response display (SSE-native) | ❌ Not implemented |
| FR-09-2 | Provider selector (manual override of routing destination via `X-LlmGateway-Provider`) | ❌ Not implemented |
| FR-09-3 | Provider health status indicators (green/amber/red per provider, fed by `/admin/providers`) | ❌ Not implemented |
| FR-09-4 | Session/conversation history sidebar (reads from `/admin/sessions` or `ISessionStore`) | ❌ Not implemented |
| FR-09-5 | Token usage counter per message (reads `usage` from SSE final chunk or `/admin/usage`) | ❌ Not implemented |
| FR-09-6 | API key / provider configuration screen (read-only for provider config in v1; edit-in-place is a stretch) | ❌ Not implemented |
| FR-09-7 | MCP server management: add/remove servers, view available tools (consumes `/admin/mcp`) | ❌ Not implemented |
| FR-09-8 | Agent picker and run console (lists `IAgentAdapter` agents, starts runs, streams `AgentEvent`s) | ❌ Not implemented |
| FR-09-9 | Authenticated against the same API-key / JWT scheme as the `/v1` endpoint (FR-04) | ❌ Not implemented |
| FR-09-10 | Consumes the API via Aspire service discovery (`https+http://api`) — no hardcoded URLs | ❌ Not implemented |

### FR-10 — Admin API

| ID | Requirement | Current State |
|---|---|---|
| FR-10-1 | `GET /admin/providers` — list all providers with health status and config | ❌ Not implemented |
| FR-10-2 | `PATCH /admin/providers/{name}` — enable/disable provider at runtime | ❌ Not implemented |
| FR-10-3 | `GET /admin/routing` — current routing rules and fallback chains | ❌ Not implemented |
| FR-10-4 | `PUT /admin/routing` — update routing rules without restart | ❌ Not implemented |
| FR-10-5 | `GET /admin/mcp` — list connected MCP servers and their tools | ❌ Not implemented |
| FR-10-6 | `POST /admin/mcp` — register a new MCP server at runtime | ❌ Not implemented |

### FR-11 — Microsoft Agent Framework DevUI

Interactive developer surface for agents and workflows, provided by the **Microsoft Agent Framework** DevUI NuGet (`MapDevUI()` ASP.NET Core middleware). It is **not** an Aspire feature and **not** the Aspire Dashboard — the two are orthogonal. See [research/https-github-com-microsoft-agent-framework.md](../research/https-github-com-microsoft-agent-framework.md) and [research/https-github-com-microsoft-agent-framework-samples.md](../research/https-github-com-microsoft-agent-framework-samples.md).

| ID | Requirement | Current State |
|---|---|---|
| FR-11-1 | Mount DevUI middleware inside `Blaze.LlmGateway.Api` via `app.MapDevUI()` on a dedicated route (`/devui` by default) | ❌ Not implemented |
| FR-11-2 | DevUI discovers every `IAgentAdapter` registered with the agent plane (from ADR-0006) — local Agent Framework agents + Foundry hosted agents | ❌ Not implemented |
| FR-11-3 | DevUI visualizes agent runs: prompt, tool calls, intermediate messages, final response, token usage, trace timeline | ❌ Not implemented |
| FR-11-4 | DevUI renders workflow graphs for `Microsoft.Agent.Framework` workflows (sequential, concurrent, handoff, magentic patterns) | ❌ Not implemented |
| FR-11-5 | DevUI consumes the same OpenTelemetry traces emitted by the agent span schema (NFR-04) — one trace ledger, two consumers (DevUI + Aspire Dashboard) | ❌ Not implemented |
| FR-11-6 | DevUI is **enabled only in `Development` environment by default**; production hosts require an explicit `LlmGateway:DevUi:Enabled=true` config flag plus auth scope `devui` on the caller | ❌ Not implemented |
| FR-11-7 | When enabled in non-dev, DevUI calls enter the normal pipeline — subject to auth (FR-04), cloud-escalation policy (ADR-0008), and rate limits (FR-07) | ❌ Not implemented |
| FR-11-8 | DevUI is reachable both directly (`https://<api-host>/devui`) and via a cross-link from the Blazor UI (FR-09) | ❌ Not implemented |
| FR-11-9 | DevUI is listed as a discoverable resource in the Aspire Dashboard so developers can navigate to it without memorizing the URL | ❌ Not implemented |
| FR-11-10 | No agent secrets, API keys, or client-identity tokens are rendered in the DevUI UI — they are redacted in trace payloads | ❌ Not implemented |
| FR-11-11 | DevUI mount is covered by a Tier-B integration test (`DistributedApplicationTestingBuilder`, NFR-05) that spins up the AppHost, fetches `/devui` over the Api resource's HTTP client, and asserts a 200 response and presence of the DevUI HTML shell | ❌ Not implemented |
| FR-11-12 | DevUI agent-discovery is covered by a Tier-B integration test that registers a stub `IAgentAdapter`, spins up the AppHost, and asserts the agent appears in DevUI's agent-list endpoint | ❌ Not implemented |
| FR-11-13 | DevUI gating is covered by a negative Tier-B test: with `LlmGateway:DevUi:Enabled=false` and `Production` environment, requests to `/devui` return 404 (per NFR-03) | ❌ Not implemented |
| FR-11-14 | DevUI cloud-escalation enforcement is covered by a Tier-B test: a `Denied`-policy client invoking an agent through DevUI receives the same `provider_not_allowed` error as a direct `/v1` caller (ADR-0008) | ❌ Not implemented |

---

## 6. Non-Functional Requirements

### NFR-01 — Performance
- Routing middleware overhead: < 1 ms (exclusive of provider call latency).
- Meta-router call (Ollama classifier) must be < 500 ms or it should short-circuit to keyword fallback.
- Streaming: first token from provider must reach client within 5 seconds under normal conditions.
- Gateway must not buffer streaming responses — chunks must be forwarded as received.

### NFR-02 — Reliability
- Gateway must remain available and return from the fallback chain even if all cloud providers are degraded (at minimum: degrade to local Ollama).
- Unhandled exceptions in routing must never crash the host process — catch at the endpoint level and return 500 with a structured error body.
- `McpConnectionManager` startup failures must be non-fatal (log and continue without tools).

### NFR-03 — Security
- No API keys or secrets stored in source control.
- All secrets injected via Aspire parameters (user-secrets in dev, Key Vault / env vars in production).
- Gateway endpoint must support HTTPS in production.
- Auth middleware must be applied before routing — unauthenticated requests must return 401 before any LLM call is made.
- **DevUI (FR-11) must be off by default outside `Development`.** Enabling it in staging/production requires both the `LlmGateway:DevUi:Enabled=true` config flag and an authenticated client carrying the `devui` scope. Unauthenticated or unscoped requests to the DevUI route must return 404 (indistinguishable from "not mounted") rather than 401, to avoid confirming the surface exists.
- **DevUI must not display secrets.** Client API keys, provider API keys, Azure credentials, and OAuth tokens are redacted from any trace or event the DevUI renders.
- Blazor consumer UI (FR-09) must authenticate against the same scheme as `/v1` — anonymous access to the chat page is not allowed when `LlmGateway:Auth:Required=true`.

### NFR-04 — Observability
- Every request must emit an OpenTelemetry span with attributes: provider selected, routing strategy used, input token count, output token count, latency.
- All provider failures must be logged at Warning or Error level with structured fields.
- Zero tolerance for `CS8600–CS8629` nullable warnings — all code must build with `-warnaserror`.

### NFR-05 — Testability
- 95% line coverage target maintained.
- All routing strategies must be testable in isolation via `IRoutingStrategy` mock.
- Circuit breaker state machine must have dedicated unit tests for all state transitions.
- Integration tests must verify SSE streaming produces valid `text/event-stream` output.
- **Aspire-orchestrated integration tests.** The Tests project must reference `Aspire.Hosting.Testing` and use `DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>(...)` to spin up the full AppHost (Api + Web + Ollama container + Foundry Local + GitHub Models) inside an xUnit test. This is the canonical end-to-end harness — anything that must observe cross-resource behavior (service discovery, secret injection, DevUI mount, Aspire-Dashboard resource wiring) belongs here, not in `WebApplicationFactory<Program>` tests.
- **Two integration-test tiers — kept separate.** Tier A: `WebApplicationFactory<Program>` against the Api project alone (fast; covers SSE contract, MCP tool round-trip, session round-trip, error contract). Tier B: `DistributedApplicationTestingBuilder` against the AppHost (slower; covers anything Aspire-orchestrated — service discovery, container readiness, DevUI mount, cross-project HTTP). CI runs both; developers can run Tier A on every save and Tier B on demand.
- **A Tier-B smoke test must exist from Phase 1**, even before there are Phase-3 features to test against — minimally: spin up the AppHost, fetch `GET /alive` on the Api resource, assert 200. This catches the class of bug where the Api forgets to call `AddServiceDefaults()` and silently loses telemetry.

### NFR-06 — Extensibility
- Adding a new LLM provider must require: (a) adding a `RouteDestination` enum value, (b) registering a keyed `IChatClient` in `AddLlmProviders`, (c) adding config options class. No other changes.
- Adding new pipeline middleware must require only implementing `DelegatingChatClient` and inserting it into `AddLlmInfrastructure`.
- Routing strategies must remain swappable via `IRoutingStrategy` — no direct class references in `LlmRoutingChatClient`.

### NFR-07 — Deployment
- Must support running via `dotnet run` (local dev), Aspire AppHost (local orchestration), and Docker container.
- All provider configuration must be injectable via environment variables with no code changes.
- Target deployment: Azure Container Apps or Azure Kubernetes Service.

---

## 7. System Boundaries and Integrations

### 7.1 Inbound (Clients)
- Any OpenAI-SDK-compatible client (Python `openai`, JS `openai`, .NET `OpenAIClient`, curl).
- **Blaze.LlmGateway.Web** (Blazor consumer UI) — via Aspire service discovery, `HttpClient`.
- **Microsoft Agent Framework DevUI** — hosted inside `Blaze.LlmGateway.Api` (same process) via `MapDevUI()`. Does not cross a network boundary; subject to the same auth pipeline as external clients.
- **Aspire Dashboard** — observes the host via OpenTelemetry / resource model; does not call `/v1`.
- ConsoleClient project — ad-hoc testing.

### 7.2 Outbound (Providers)

| Provider | Transport | Auth | Notes |
|---|---|---|---|
| Azure OpenAI (Foundry) | HTTPS | ApiKey or DefaultAzureCredential | Primary enterprise provider |
| Ollama (remote 192.168.16.56) | HTTP | None | Used as meta-router (model: `router`) and general chat |
| OllamaBackup | HTTP | None | Same host, different model (`llama3.2`) |
| GitHub Copilot | HTTPS | Bearer token | `https://api.githubcopilot.com` |
| Gemini | HTTPS | ApiKey | Google GenAI SDK |
| OpenRouter | HTTPS | ApiKey | OpenAI-compatible; default model: `qwen/qwen3-235b-a22b:free` |
| Azure Foundry Local | HTTP (localhost) | `"notneeded"` | Phi-4-Mini via Aspire FoundryLocal |
| GitHub Models | HTTPS | PAT | `https://models.inference.ai.azure.com`; `gpt-4o-mini`, `phi-4-mini-instruct` |
| OllamaLocal | HTTP (localhost) | None | Local Ollama container via Aspire; backup for remote |

### 7.3 MCP Servers

| Server | Transport | Notes |
|---|---|---|
| `microsoft-learn` | Stdio (npx) | Microsoft Learn documentation search |
| _(future)_ | HTTP | Any additional MCP server registered via config |

---

## 8. Data Models (Current)

### 8.1 Request / Response (Wire Format)
The gateway accepts and emits OpenAI-compatible JSON. No custom schema is defined — the endpoint parses `messages[].role` and `messages[].content` from the request body and streams back `choices[].delta.content` chunks.

**Gap:** No model is bound for the request body — it is parsed ad-hoc via `JsonDocument`. A typed `ChatCompletionRequest` model should be introduced.

### 8.2 Configuration (`LlmGatewayOptions`)

```
LlmGatewayOptions
├── Providers (ProvidersOptions)
│   ├── AzureFoundry  { Endpoint, Model, ApiKey? }
│   ├── Ollama        { BaseUrl, Model }
│   ├── OllamaBackup  { BaseUrl, Model }
│   ├── GithubCopilot { Endpoint, ApiKey, Model }
│   ├── Gemini        { ApiKey, Model }
│   ├── OpenRouter    { Endpoint, ApiKey, Model }
│   ├── FoundryLocal  { Endpoint, Model, ApiKey="notneeded" }
│   ├── GithubModels  { Endpoint, ApiKey, Model }
│   └── OllamaLocal   { BaseUrl, Model }
└── Routing (RoutingOptions)
    ├── RouterModel           = "router"
    └── FallbackDestination   = "AzureFoundry"
```

**Gaps in data model (future requirements):**
- `Routing.FallbackChains` — ordered list per RouteDestination
- `Routing.CircuitBreaker` — threshold, cooldown per provider
- `Auth` — API key store, JWT settings
- `RateLimits` — per-key token budgets
- `Cache` — TTL, backend (memory, Redis)
- `ProviderCapabilities` — context window size, supports vision, max tokens

---

## 9. Extension Surface Map

The following areas are the primary extension points, ordered by architectural impact:

### 9.1 Routing Engine (High Impact)
- `IRoutingStrategy` — new implementations (semantic, capability-based, cost-optimized, round-robin)
- `LlmRoutingChatClient` — add failover loop, circuit breaker integration, retry logic
- `RouteDestination` — add new enum values as new providers are added
- `FallbackChains` in config — eliminate hardcoded fallback in `LlmRoutingChatClient`

### 9.2 Pipeline Middleware (Medium Impact)
- New `DelegatingChatClient` implementations: caching layer, rate-limit enforcer, token counter, auth validator, audit logger
- All middleware is inserted into `AddLlmInfrastructure` with predictable stack ordering

### 9.3 Provider Registry (Low Impact per Provider)
- New providers: add to `RouteDestination` enum, create options class, register keyed client in `AddLlmProviders`
- Provider health tracker: new singleton that circuit breakers consult

### 9.4 MCP Layer (Medium Impact)
- Full `HostedMcpServerTool` mapping in `McpToolDelegatingClient`
- Dynamic server registration API
- Per-request tool selection

### 9.5 Web UI surfaces (High Surface Area, Low Core Impact)
- **Blazor consumer UI** (`Blaze.LlmGateway.Web`): chat component, provider status widgets, session browser, admin screens. API client generated from the OpenAPI spec. All UI changes are isolated to this project.
- **Agent Framework DevUI**: enabled with a single line (`app.MapDevUI()`) in `Blaze.LlmGateway.Api`. No custom view code — the middleware renders its own UI against the registered `IAgentAdapter` set and the OTel trace feed. Extension surface is just: which agents/workflows to register + env gating.
- **Aspire Dashboard**: zero extension needed; new resources (e.g. a SQLite container from ADR-0004) appear automatically once declared in `Blaze.LlmGateway.AppHost`.

### 9.6 Admin API (Medium Impact)
- New controller/endpoint group in `Blaze.LlmGateway.Api`
- Reads from `IOptionsMonitor<LlmGatewayOptions>` for live config
- Protected by admin-scoped API key or role claim

---

## 10. Open Questions (To Resolve Before Planning Sprints)

| # | Question | Stakeholder |
|---|---|---|
| OQ-1 | Should the gateway be multi-tenant (separate provider pools per tenant/team) or single-tenant? | Product |
| OQ-2 | Is response caching a core feature or a pluggable add-on (separate NuGet package)? | Architecture |
| OQ-3 | What is the target deployment environment — Azure Container Apps, AKS, self-hosted? | Ops |
| OQ-4 | Should conversation sessions be stored in-process (memory) or externally (Redis, Cosmos DB)? | Architecture |
| OQ-5 | ~~Is the Blazor Web UI for internal admin only, or a consumer-facing product?~~ **Resolved 2026-04-17: consumer-facing chat + admin surface** (FR-09 expanded). Internal-only admin is deferred to a dedicated admin-only build flag if ever needed. | Product |
| OQ-6 | What is the token cost tracking source of truth — provider APIs, metered billing, or estimated from token counts? | Finance/Product |
| OQ-7 | Should the Admin API be separate from the chat API (different port / service), or co-hosted? | Architecture |
| OQ-8 | Is streaming failover (mid-stream) required for the first resilience sprint, or is pre-stream failover sufficient? | Product |
| OQ-9 | Will there be an SDK / client library for .NET consumers, or is OpenAI-compatibility sufficient? | Developer Experience |
| OQ-10 | How should the meta-router Ollama model be deployed in production — sidecar container, separate service, or cloud model? | Ops |
| OQ-11 | Should the Agent Framework DevUI (FR-11) ship in production images, or be stripped from production builds entirely? Two options: (a) compile-time `#if DEBUG` / `DevUI` trim-out, (b) runtime gate only. Default assumption is (b) — runtime gate with `Development` default-enabled, production default-disabled. | Security, Ops |

---

## 11. Assumptions

- The `Microsoft.Extensions.AI` (MEAI) API surface is stable and will not introduce breaking changes before v1.
- The Ollama meta-router model (`router`) will be available as a network service or local container in all deployment environments.
- `dotnet user-secrets` / Aspire parameter injection is the accepted secret management strategy for local dev; Azure Key Vault for production.
- The Syncfusion Blazor license will remain available; the Web UI will use Syncfusion components for chat and data grid surfaces.
- All LLM provider SDKs (`Azure.AI.OpenAI`, `OllamaSharp`, `Google.GenAI`, `OpenAI`) will continue to support `.AsIChatClient()` MEAI adapters.
- The **Microsoft Agent Framework DevUI** NuGet package (ASP.NET middleware form) will be published and maintained by Microsoft; the .NET workflow-DevUI parity gap with the Python package will be closed before Phase 3 opens. If parity slips, FR-11-4 (workflow graph rendering) degrades to "agent runs only" until it lands.
- The Aspire Dashboard is provided by `.NET Aspire` and remains the canonical platform-telemetry surface — no in-repo replacement will be built.
