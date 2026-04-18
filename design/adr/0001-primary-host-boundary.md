# ADR-0001: Primary host boundary — gateway core and agent runtime co-host

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture
- **Related:** ADR-0002, ADR-0003, ADR-0006, [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §Phase 0, [PRD](../../PRD/blaze-llmgateway-prd.md) §2, §9

## Context

Blaze.LlmGateway today is a single `Blaze.LlmGateway.Api` host (minimal API) fronting a `Microsoft.Extensions.AI` (MEAI) pipeline with keyed provider clients. The north-star plan evolves it into an **LLM engine + agent runtime** serving chatbots, personal-agent projects, Copilot CLI BYOM, Microsoft Agent Framework hosts, and Azure Foundry agents. Two realistic shapes for that evolution:

1. **Monolithic "platform" host** — gateway routing, agent runtime, tool plane, admin API, all in one process.
2. **Split** — gateway as one process, agent runtime as a separate process, linked by HTTP/A2A.

Splitting adds operational cost (two deploy units, inter-process auth, observability correlation) and a real risk that the agent plane ends up reimplementing provider and tool logic. Co-hosting risks the host becoming an undifferentiated monolith unless internal layering is enforced.

The planning draft already confirmed **primary API host owns both gateway and agent runtime** in §"Confirmed planning choices". This ADR locks that in and specifies how internal layering is enforced.

## Decision

We will **co-host the inference, tool, agent, and integration planes inside a single primary API host (`Blaze.LlmGateway.Api`)**, with plane boundaries enforced at the .NET project level and only one-way dependencies permitted.

### Details

**Project layout (after Phase 1–3).** Arrow = "references".

```
Blaze.LlmGateway.Api                          ← composition root, ASP.NET host, controllers
  ├─► Blaze.LlmGateway.Integrations           ← OpenAI-compat DTOs, Copilot SDK sample glue, A2A (future)
  ├─► Blaze.LlmGateway.Agents                 ← agent plane: sessions, workflows, Agent Framework adapters
  ├─► Blaze.LlmGateway.Infrastructure         ← inference + tool planes: providers, routing, MCP, middleware
  ├─► Blaze.LlmGateway.Persistence            ← ISessionStore, EF Core DbContext (see ADR-0004)
  └─► Blaze.LlmGateway.Core                   ← domain types, configuration records, no external deps

Blaze.LlmGateway.Web       → references Api (via Aspire service discovery)
Blaze.LlmGateway.AppHost   → orchestrates Api + Web + Ollama + Foundry Local
Blaze.LlmGateway.Tests     → references all of the above
```

**Enforcement rules.**

1. `Core` has **zero** references to other Blaze projects (unchanged from today).
2. `Persistence` may reference only `Core`.
3. `Infrastructure` may reference `Core` and `Persistence`. It **must not** reference `Agents` or `Integrations`.
4. `Agents` may reference `Core`, `Persistence`, and `Infrastructure` (to obtain `IChatClient` and tool surfaces). It **must not** reference `Integrations`.
5. `Integrations` may reference `Core`, `Infrastructure`, and `Agents`.
6. `Api` is the only project that composes all of them (DI wiring).

**Enforcement mechanism.** Add a NetArchTest-based fixture in `Blaze.LlmGateway.Tests` that asserts the dependency rules above. Fails the build (which uses `-warnaserror`) if anyone references across a boundary.

**Single-process deployment unit.** The Api project produces one container image. Aspire AppHost provisions the image plus external resources (Ollama, Foundry Local, Postgres for sessions, any local runtime container).

## Consequences

**Positive**

- One deploy unit, one config, one auth surface, one observability pipeline.
- Agent plane can directly reuse the keyed `IChatClient` registry and MCP tool cache — no duplication.
- Internal layering is mechanically enforced, so the host does not decay into a monolith by accident.
- Aligns with `Microsoft.Agent.Framework` samples where `ChatClientAgent` wraps a locally-resolved `IChatClient`.

**Negative**

- A runaway agent run (tight tool-call loop, OOM, thread starvation) can affect the gateway's inference path. Mitigation: per-session resource quotas (Phase 5), agent runs scheduled on a separate `TaskScheduler` / queue, readiness/liveness probes.
- Harder to scale the two planes independently. If agent workloads grow CPU-heavy we may need to re-open this ADR and extract the agent plane.

**Neutral**

- Adds two new projects (`Agents`, `Persistence`) and one sample integration project in Phase 1–3. Solution file ([Blaze.LlmGateway.slnx](../../Blaze.LlmGateway.slnx)) must be updated.
- Tests project gains an architecture-assertion fixture — one-time cost.

## Alternatives Considered

### Alternative A — Separate agent runtime process

Run `Blaze.LlmGateway.Agents.Host` as a second ASP.NET service, linked to the gateway via HTTP. **Rejected** because:

- Inter-process hop for every tool invocation doubles latency and requires a serialization contract for `IChatClient` + MCP tools.
- Duplicate provider config / secret handling.
- Two observability pipelines to correlate.
- Planning draft already confirmed co-hosting is the desired direction.

### Alternative B — Library-only agent plane

Ship `Blaze.LlmGateway.Agents` as a NuGet package consumed by external hosts (no agent runtime inside the gateway host itself). **Rejected** because:

- Defeats the "internal LAN LLM engine" goal — each consumer would have to host its own agent runtime.
- Makes durable session storage a per-consumer problem, which undermines ADR-0004.
- Still leaves the question of how the gateway's own admin/chat UI runs agents.

We may re-introduce "also ship as a library" as a follow-on once the in-host runtime is stable. That is additive to this decision, not a replacement.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §3 Architecture overview
- [../../plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Proposed north-star architecture"
- [../../research/https-github-com-microsoft-agent-framework.md](../../research/https-github-com-microsoft-agent-framework.md) — `ChatClientAgent` composition pattern
- [NetArchTest](https://github.com/BenMorris/NetArchTest) — architecture assertions
