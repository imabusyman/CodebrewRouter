# design/

Engineer-ready technical design and architecture decisions for **Blaze.LlmGateway**.

This folder is a peer to [`PRD/`](../PRD) (requirements) and [`plan/`](../plan) (north-star direction). It bridges the two into an implementable blueprint.

## Layout

```
design/
├── README.md                                     ← you are here
├── tech-design/
│   └── blaze-llmgateway-architecture.md          ← master design doc
└── adr/
    ├── 0000-adr-template.md
    ├── 0001-primary-host-boundary.md
    ├── 0002-provider-identity-model.md
    ├── 0003-northbound-api-surface.md
    ├── 0004-session-state-persistence.md
    ├── 0005-local-runtime-compatibility.md
    ├── 0006-azure-foundry-agents-integration.md
    ├── 0007-copilot-ecosystem-strategy.md
    └── 0008-cloud-escalation-policy.md
```

## How to read this folder

1. **Start** with [tech-design/blaze-llmgateway-architecture.md](tech-design/blaze-llmgateway-architecture.md). It is the single source of truth for the four-plane architecture (inference / tool / agent / integration), component contracts, DTO schemas, deployment topology, and phasing.
2. **Drill into** an individual ADR under [adr/](adr) when the master doc references it. Each ADR follows the Michael Nygard format (Status · Context · Decision · Consequences · Alternatives · References) and closes with a **Recommended** decision.
3. **Cross-check** every FR/NFR in [../PRD/blaze-llmgateway-prd.md](../PRD/blaze-llmgateway-prd.md) against the traceability table in §2 of the master doc.

## ADR index

| # | Title | Recommendation | Status |
|---|---|---|---|
| [0001](adr/0001-primary-host-boundary.md) | Primary host boundary | Gateway core + agent runtime co-hosted in the Api project, strict project-level layering | Proposed |
| [0002](adr/0002-provider-identity-model.md) | Provider identity model | Config-driven `ProviderDescriptor` + `ModelProfile`; migrate off `RouteDestination` enum | Proposed |
| [0003](adr/0003-northbound-api-surface.md) | Northbound API surface | Phase 1 = hardened OpenAI Chat Completions only; Responses/A2A deferred | Proposed |
| [0004](adr/0004-session-state-persistence.md) | Session state persistence | SQLite + EF Core `ISessionStore` abstraction; swap to Postgres/Cosmos later | Proposed |
| [0005](adr/0005-local-runtime-compatibility.md) | Local runtime compatibility | LM Studio + llama.cpp via generic OpenAI-compat catalog entries, no special adapter | Proposed |
| [0006](adr/0006-azure-foundry-agents-integration.md) | Azure Foundry agents | `IAgentAdapter` pattern fronting Foundry hosted/local agents | Proposed |
| [0007](adr/0007-copilot-ecosystem-strategy.md) | Copilot ecosystem strategy | Phase 1 = BYOM + one Copilot SDK sample; plugin/MCP packaging deferred | Proposed |
| [0008](adr/0008-cloud-escalation-policy.md) | Cloud escalation policy | Default-deny for cloud providers; per-client auth-bound allow-list | Proposed |

## Source materials

- [../PRD/blaze-llmgateway-prd.md](../PRD/blaze-llmgateway-prd.md) — FRs, NFRs, open questions.
- [../plan/llm-agent-platform-plan.md](../plan/llm-agent-platform-plan.md) — north-star planning draft.
- [../CLAUDE.md](../CLAUDE.md) — project-wide coding conventions and architectural rules.
- [../research/](../research) — reference patterns cited inline by the master doc.

## Status

All ADRs and the master doc are **Proposed**. Promote to **Accepted** once the design is signed off.
