# ADR-0006: Azure Foundry agents — `IAgentAdapter` pattern for hosted and local agents

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture
- **Related:** ADR-0001, ADR-0002, ADR-0004, [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Phase 3 - Agent integration layer"

## Context

Azure Foundry exposes agents in two forms:

1. **Foundry hosted agents** — agents defined in the Azure AI Foundry service, identified by an Azure-resource ID, invoked via the Foundry Agents SDK. Their chat history, tools, and tool execution run in Azure.
2. **Foundry local agents** — agents defined inline in application code, composed with `Microsoft.Agent.Framework`'s `AIAgent` / `ChatClientAgent` and executed in-process.

The north-star plan wants both integrated into the Blaze.LlmGateway agent plane. The tricky part is hosting-mode heterogeneity: hosted agents own their tool loop and state in Azure, while local agents rely on the gateway's session store (ADR-0004), MCP tool plane, and inference plane.

If we expose these two modes as distinct top-level APIs (e.g. `/v1/foundry-hosted/...` vs `/agents/...`), the integration plane gets a cartesian product problem: every downstream client needs to know which kind of agent it is talking to, and duplicated invocation plumbing proliferates.

## Decision

We will **front both hosted and local Foundry agents with a uniform `IAgentAdapter` contract**, so that the integration plane, session store, and northbound API treat all agents identically. The adapter hides whether a given agent runs in Azure, in-process via Agent Framework, or elsewhere.

### Details

**Core contract.** Lands in `Blaze.LlmGateway.Agents`:

```csharp
public interface IAgentAdapter
{
    string AgentId { get; }
    AgentKind Kind { get; }                     // Local | FoundryHosted | External
    AgentDescriptor Descriptor { get; }

    IAsyncEnumerable<AgentEvent> RunAsync(
        AgentRunRequest request,
        CancellationToken ct = default);
}

public sealed record AgentDescriptor(
    string AgentId,
    string DisplayName,
    AgentKind Kind,
    IReadOnlyList<string> SupportedToolNamespaces,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record AgentRunRequest(
    string SessionId,                           // ISessionStore key (ADR-0004)
    IReadOnlyList<ChatMessage> NewMessages,
    AgentRunOptions? Options);

public abstract record AgentEvent
{
    public sealed record MessageDelta(string Text) : AgentEvent;
    public sealed record ToolCallStarted(string ToolName, string InvocationId) : AgentEvent;
    public sealed record ToolCallCompleted(string InvocationId, string ResultJson) : AgentEvent;
    public sealed record RunCompleted(string FinishReason, Usage? Usage) : AgentEvent;
    public sealed record RunFailed(string Message, string? Code) : AgentEvent;
}

public enum AgentKind { Local, FoundryHosted, External }
```

**Two Phase-3 implementations.**

1. `AgentFrameworkAdapter` — wraps `ChatClientAgent` from `Microsoft.Agent.Framework`. Consumes:
   - `IChatClient` from the inference plane (via `IProviderRegistry.GetChatClient(modelId)` from ADR-0002).
   - `ISessionStore` (ADR-0004) for message persistence.
   - `McpConnectionManager` tools (as MEAI `AITool` instances).

2. `FoundryHostedAgentAdapter` — wraps Azure AI Foundry's hosted-agent SDK. Consumes:
   - Azure credential (`DefaultAzureCredential` or key from config).
   - Foundry `AgentId` (Azure resource).
   - Translates Foundry's stream events into `AgentEvent` discriminated union.
   - Mirrors a read-only copy of messages into `ISessionStore` for local observability — **does not** try to own the truth (Foundry does).

**Registry.** An `IAgentRegistry` resolves `AgentDescriptor`s from config:

```csharp
public interface IAgentRegistry
{
    IReadOnlyCollection<AgentDescriptor> List();
    IAgentAdapter Resolve(string agentId);
}
```

Config shape:

```json
{
  "LlmGateway": {
    "Agents": [
      { "AgentId": "local/support-agent", "Kind": "Local", "ModelId": "ollama-lan/llama3.2", "ToolNamespaces": ["microsoft-learn", "azure-docs"] },
      { "AgentId": "foundry/triage",      "Kind": "FoundryHosted", "AzureAgentId": "asst_abc123", "AzureProjectEndpoint": "https://...", "ToolNamespaces": ["microsoft-learn"] }
    ]
  }
}
```

**Integration-plane surface (deferred endpoints, listed for context).** Phase 3 adds:

- `POST /agents` — list available agents.
- `POST /agents/{agentId}/runs` — start a run; returns `runId`.
- `GET  /agents/{agentId}/runs/{runId}` — stream events (SSE; maps 1:1 to `AgentEvent`).

These APIs are **not** Phase 1. Per ADR-0003, Phase-1 Chat Completions is the only northbound surface. But the adapter contract lands in Phase 3 and the session store (ADR-0004) lands in Phase 1 to unblock it.

**State ownership.**

| Data | `Local` adapter | `FoundryHosted` adapter |
|---|---|---|
| Conversation history | `ISessionStore` is source of truth | Foundry is source of truth; gateway caches for UI |
| Tool registry | Gateway's MCP plane | Foundry's tool bindings (may overlap with gateway MCP) |
| Tool invocation records | `tool_invocation` table | Copy into `tool_invocation` for audit; mark `source='foundry'` |
| Run checkpoints (`dafx-`) | `agent_run` table | Foundry run IDs stored as `metadata.foundry_run_id` |

**Observability.** `AgentEvent` stream feeds the OTel agent span schema (defined in the master doc §8). Spans are the same regardless of adapter kind.

## Consequences

**Positive**

- Integration plane treats all agents as a single abstraction. One endpoint shape, one client compatibility story.
- The agent plane can mix-and-match local and hosted agents in the same workflow (Phase 3+ workflow orchestration becomes trivially possible).
- Foundry-specific complexity is quarantined inside `FoundryHostedAgentAdapter`.

**Negative**

- Some Foundry-only features (private thread attachments, code-interpreter with sandbox files) don't map cleanly onto the unified `AgentEvent` stream. We add `AgentEvent.Extension(string kind, string payloadJson)` for those, documented per adapter.
- Two sources of truth for hosted-agent history (Foundry + gateway cache) can diverge if Foundry's thread is modified outside the gateway. We document that hosted agents remain authoritative and the gateway cache is best-effort.

**Neutral**

- Adds roughly 8 new types under `Blaze.LlmGateway.Agents/`. Nothing lands in Phase 1 besides placeholder `IAgentAdapter` and `AgentDescriptor` contracts (referenced by `ISessionStore` for cross-plane session shape).

## Alternatives Considered

### Alternative A — Treat Foundry hosted agents as opaque `IChatClient`s

Register a hosted agent as just another provider in the catalog. **Rejected** — hosted agents already run their own tool loop, stream non-message events (reasoning steps, tool-call annotations), and have thread identity separate from chat turns. Squeezing them into `IChatClient` loses all of that.

### Alternative B — Distinct top-level API per agent kind

`/v1/foundry/runs` vs `/v1/agents/runs`. **Rejected** — clients must branch based on where an agent is deployed, which is exactly the implementation detail the adapter is meant to hide. Makes migration between hosting modes a client breaking change.

### Alternative C — Defer the hosted-agent case to Phase 4+

Ship only local Agent Framework adapters in Phase 3. **Considered**, and in fact scheduling-wise the `FoundryHostedAgentAdapter` can land later than `AgentFrameworkAdapter`. But the `IAgentAdapter` contract must be designed with both in mind from day one, otherwise we retrofit hosted-agent eventing onto a local-agent-shaped interface and break clients.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §6 Agent plane
- [../../research/https-github-com-microsoft-agent-framework.md](../../research/https-github-com-microsoft-agent-framework.md) — `ChatClientAgent`, workflows, durable runs
- [../../research/https-github-com-microsoft-agent-framework-samples.md](../../research/https-github-com-microsoft-agent-framework-samples.md) — Foundry Local & hosted-agent samples
- [../../research/https-github-com-microsoft-foundry-local.md](../../research/https-github-com-microsoft-foundry-local.md) — Foundry Local agent patterns
- [Azure AI Foundry agents](https://learn.microsoft.com/azure/ai-services/agents/overview)
