# ADR-0004: Session state persistence — SQLite + EF Core `ISessionStore` abstraction

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture
- **Related:** ADR-0001, ADR-0003, ADR-0006, [PRD §5 FR-08, §10 OQ-4](../../PRD/blaze-llmgateway-prd.md), [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Critical ADRs required before implementation §4"

## Context

The north-star plan locked **"durable persisted sessions first"** as a phase-1 requirement, not a later enhancement (see "Confirmed planning choices"). The PRD captures this in FR-08 and raises OQ-4: *"in-process memory, Redis, or Cosmos DB?"*.

What a durable session must hold in Phase 1:

1. **Multi-turn chat history** — ordered list of `ChatMessage` for a single `session_id`.
2. **Tool-call records** — each invocation's request, response, latency, provider, and outcome, for later inspection and replay.
3. **Agent run state** (Phase 3+) — the planning draft and Agent Framework samples both expect durable agent state checkpointing using the `dafx-` ID convention from `research/https-github-com-microsoft-agent-framework.md`.
4. **Client and routing metadata** — which client issued the session, which provider(s) handled each turn, cost/token counters.

Constraints specific to this product:

- LAN-first deployment — the default target is a single server on an internal network. Requiring an external Postgres/Redis/Cosmos instance raises the barrier to "just run the gateway".
- Aspire AppHost already runs local container dependencies (Ollama, Foundry Local). Adding a database container is cheap for dev but should not be mandatory at runtime.
- Writes are low-volume compared to provider calls (a session turn writes once per user turn and once per assistant turn; a session has tens to low thousands of turns at most).
- Queries are always scoped by `session_id` or `client_id`. No large cross-session OLAP.
- Schema will evolve (agent-run state added in Phase 3, usage/cost counters in Phase 5). Migration-friendly storage is a must.

## Decision

We will **ship SQLite (file-backed) as the default persistence, behind an `ISessionStore` abstraction implemented with EF Core**. The same abstraction will be re-implementable against Postgres (Phase 5 cloud target) or Azure Cosmos DB (multi-region) without touching the agent or integration planes.

### Details

**New project.** `Blaze.LlmGateway.Persistence` (references only `Core`). Contents:

```
Blaze.LlmGateway.Persistence/
├── ISessionStore.cs
├── SessionStore.cs                 ← EF Core implementation
├── GatewayDbContext.cs
├── Migrations/                     ← dotnet ef migrations
└── Entities/
    ├── SessionEntity.cs
    ├── SessionMessageEntity.cs
    ├── ToolInvocationEntity.cs
    ├── AgentRunEntity.cs           ← Phase 3
    └── UsageCounterEntity.cs       ← Phase 5
```

**Core abstraction.**

```csharp
public interface ISessionStore
{
    Task<Session> CreateAsync(SessionDescriptor descriptor, CancellationToken ct);
    Task<Session?> GetAsync(string sessionId, CancellationToken ct);
    Task AppendMessageAsync(string sessionId, ChatMessage message, SessionTurnMetadata meta, CancellationToken ct);
    Task RecordToolInvocationAsync(string sessionId, ToolInvocationRecord record, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string sessionId, int? maxTurns, CancellationToken ct);
    Task UpdateMetadataAsync(string sessionId, IDictionary<string, string> metadata, CancellationToken ct);
    IAsyncEnumerable<SessionSummary> ListAsync(ClientIdentity clientId, int page, int pageSize, CancellationToken ct);
    Task DeleteAsync(string sessionId, CancellationToken ct);
}

public sealed record SessionDescriptor(
    string? SessionId,                  // null => generate "sess_" + ULID
    ClientIdentity ClientId,
    string? Title,
    IReadOnlyDictionary<string, string>? Metadata,
    TimeSpan? Ttl);

public sealed record SessionTurnMetadata(
    string ProviderId,
    string ModelId,
    string RoutingStrategy,
    int InputTokens,
    int OutputTokens,
    TimeSpan Latency);
```

**Session ID convention.** `sess_<ULID>` for user sessions; `dafx_<ULID>` for Durable-Agent-Framework runs (matches the Agent Framework sample naming). ULIDs preserve creation-order sorting without a separate index.

**Default storage path.** `Data Source={ApplicationData}/Blaze.LlmGateway/sessions.db` for dev, overridable via `LlmGateway:Persistence:ConnectionString`. Aspire AppHost will mount a named volume so dev sessions survive restarts.

**Schema sketch.**

```sql
-- session
id TEXT PRIMARY KEY,                       -- "sess_..." or "dafx_..."
client_id TEXT NOT NULL,
title TEXT,
metadata_json TEXT,                        -- free-form JSON
created_at INTEGER NOT NULL,               -- unix ms
last_active_at INTEGER NOT NULL,
ttl_seconds INTEGER                        -- NULL => no expiry

-- session_message
id TEXT PRIMARY KEY,                       -- "msg_..."
session_id TEXT NOT NULL REFERENCES session(id) ON DELETE CASCADE,
seq INTEGER NOT NULL,                      -- turn order
role TEXT NOT NULL,                        -- system | user | assistant | tool
content_json TEXT NOT NULL,                -- serialized ChatMessage (supports tool_calls etc.)
provider_id TEXT,                          -- provider that produced assistant/tool message
model_id TEXT,
routing_strategy TEXT,
input_tokens INTEGER, output_tokens INTEGER,
latency_ms INTEGER,
created_at INTEGER NOT NULL,
UNIQUE (session_id, seq)

-- tool_invocation
id TEXT PRIMARY KEY,                       -- "tool_..."
session_id TEXT NOT NULL REFERENCES session(id) ON DELETE CASCADE,
message_id TEXT REFERENCES session_message(id),
tool_name TEXT NOT NULL,
mcp_server_id TEXT,
request_json TEXT NOT NULL,
response_json TEXT,
status TEXT NOT NULL,                      -- pending | success | error
error_message TEXT,
started_at INTEGER NOT NULL,
completed_at INTEGER
```

Indexes: `session(client_id, last_active_at DESC)`, `session_message(session_id, seq)`, `tool_invocation(session_id, started_at DESC)`.

**Retention and TTL.** Sessions have optional `ttl_seconds`. A hosted `SessionCleanupService` runs on a 15-minute cadence, deleting sessions where `last_active_at + ttl_seconds < now`. Defaults: no TTL unless configured; configurable per-client and globally via `LlmGateway:Persistence:DefaultTtl`.

**EF Core wiring.** `Blaze.LlmGateway.Persistence.ServiceExtensions.AddGatewayPersistence(this IServiceCollection, Action<PersistenceOptions>)`:

```csharp
services.AddDbContextPool<GatewayDbContext>(o =>
    o.UseSqlite(connectionString));                        // swap to UseNpgsql / UseCosmos in prod
services.AddSingleton<ISessionStore, SessionStore>();
services.AddHostedService<SessionCleanupService>();
services.AddHostedService<MigrationRunner>();              // applies dotnet ef migrations on startup
```

**Swap targets.** Same interface, different provider. Phase 5 options:

| Backend | Use case | Switch |
|---|---|---|
| SQLite (default) | Single-host LAN, dev, small teams | `UseSqlite(...)` |
| Postgres | Multi-host, containerized prod | `UseNpgsql(...)` |
| Cosmos DB (SQL API) | Multi-region / multi-tenant | `UseCosmos(...)` + different `DbContext` mapping |

The `ISessionStore` contract does not leak EF Core or connection details to callers. Agents and integration handlers never see the substrate.

**Testing.** Every unit test uses `UseSqlite("DataSource=:memory:")` with a fresh schema per test. Integration tests target a file-backed SQLite to exercise migrations.

## Consequences

**Positive**

- Durable sessions from day one; no runtime dependency on an external DB for LAN deployments. Solves FR-08, OQ-4, and enables the agent plane in Phase 3 without a second design round.
- Swapping to Postgres or Cosmos is an infra + wiring change, not an app rewrite.
- Schema migrations are versioned via `dotnet ef migrations`; both CI and startup apply them.
- Tool-invocation history gives us free audit trails for compliance (PRD FR-04-related requirement).

**Negative**

- SQLite is single-writer. If we scale out to multiple API hosts on the same file, writes serialize. Phase 5 must switch to Postgres/Cosmos for horizontal scale. Documented up front.
- EF Core adds ~6 MB to the deploy and a startup migration step (~100 ms on an empty DB). Negligible.
- Operators must manage `sessions.db` backups. We ship a script + doc; not free but standard SQLite practice.

**Neutral**

- `Blaze.LlmGateway.Persistence` is a new project — updates to [Blaze.LlmGateway.slnx](../../Blaze.LlmGateway.slnx), Aspire AppHost volume mount, deployment manifest.
- `ISessionStore` is consumed by the Integration plane (for Chat Completions session correlation when `X-LlmGateway-Session-Id` is present) and by the Agent plane (for agent runs). The Inference plane stays session-unaware.

## Alternatives Considered

### Alternative A — Redis only

Fast, simple, no migrations. **Rejected** — rich queries (list sessions by client, scan tool invocations) require secondary structures; complex multi-key writes (session + message + tool_invocation atomic) are awkward; requires an external service just to boot the gateway, breaking the "LAN-first, zero-dependency" story.

### Alternative B — Azure Cosmos DB from day one

Gives us multi-region later. **Rejected** — mandatory cloud dependency clashes with LAN-first; cost is non-zero; local development and tests need an emulator. We keep Cosmos DB as a documented swap path for multi-region deployments rather than the default.

### Alternative C — In-process MemoryCache only

Zero external dependency. **Rejected** — violates the "durable persisted sessions first" commitment; sessions die on restart; no audit trail.

### Alternative D — Postgres-default with Aspire container

Phase-5-ready immediately. **Rejected** for Phase 1 only — requires Docker at dev time, bumps the minimum-viable setup for new contributors. Postgres remains the recommended production backend via the `ISessionStore` abstraction.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §6 Agent plane
- [../../research/https-github-com-microsoft-agent-framework.md](../../research/https-github-com-microsoft-agent-framework.md) — durable-agent state conventions (`dafx-` IDs)
- [../../research/https-github-com-microsoft-agent-framework-samples.md](../../research/https-github-com-microsoft-agent-framework-samples.md) — session/checkpoint patterns
- [EF Core providers](https://learn.microsoft.com/ef/core/providers/) — SQLite / Npgsql / Cosmos
