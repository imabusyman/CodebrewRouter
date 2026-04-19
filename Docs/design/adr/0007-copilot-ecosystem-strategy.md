# ADR-0007: Copilot ecosystem strategy — BYOM compatibility + one SDK sample in Phase 1

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture, Product
- **Related:** ADR-0003, ADR-0008, [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Phase 1 client surfaces", §"Phase 4 - Client ecosystem integration"

## Context

The north-star plan lists four Copilot-related surfaces:

1. **Copilot CLI BYOM** — GitHub Copilot CLI can point at a custom OpenAI-compatible endpoint via its `--model` + env-var configuration. Users run `copilot` with Blaze as the backend.
2. **Copilot SDK integration sample** — a reference app that uses the GitHub Copilot SDK to orchestrate the Blaze gateway (the SDK is CLI-backed and acts as an agentic runtime — see `research/github-copilot-sdk.md`).
3. **Copilot plugin packaging** — shipping Blaze as an installable plugin inside the Copilot ecosystem (plugins manifest, MCP wiring).
4. **Copilot platform auth** — Entra / service-principal flow, per GitHub's guidance.

Shipping all four in Phase 1 would consume the bulk of an entire phase for what are essentially ecosystem artifacts rather than platform work. Shipping none of them loses the Phase-1 user story of "replace the Copilot default model with Blaze."

## Decision

We will ship **two Copilot touchpoints in Phase 1**:

1. **Copilot CLI BYOM compatibility** — documented + smoke-tested, delivered by the generic OpenAI Chat Completions surface (ADR-0003).
2. **One Copilot SDK sample project** — `samples/Blaze.LlmGateway.Samples.CopilotSdk/` — demonstrating a real Copilot SDK app talking to Blaze.

**Plugin/MCP packaging is explicitly deferred** to a follow-on ADR after Phase 4.

### Details

**Copilot CLI BYOM.** No code changes in the gateway — the Chat Completions surface from ADR-0003 is already what the CLI expects. Deliverables:

- `docs/clients/copilot-cli.md` with the exact env-var / CLI-flag setup:
  ```bash
  export COPILOT_API_BASE="http://<blaze-host>:<port>/v1"
  export COPILOT_API_KEY="sk-blaze-<key>"
  copilot --model "ollama-lan/llama3.2" "explain this repo"
  ```
- Smoke test script (`samples/compatibility-tests/copilot-cli-smoke.sh`) that runs a canned prompt and asserts a streaming response.
- A compatibility note in the master doc §7 for the caveats we discover (streaming frame shape, `finish_reason` handling).

**Copilot SDK sample.** New project:

```
samples/
└── Blaze.LlmGateway.Samples.CopilotSdk/
    ├── Program.cs                           ← console app
    ├── appsettings.json                     ← points at local Blaze
    └── README.md                            ← step-by-step
```

Scope of the sample:

- Uses the Copilot SDK (`@github/copilot` Node SDK or equivalent) to drive a small agentic task (e.g. "summarize the top 3 commits on the current branch") against Blaze.
- Demonstrates model override via `X-LlmGateway-Model`.
- Exercises the MCP tool plane by calling a tool exposed by the gateway (the existing `microsoft-learn` MCP server).

The sample lives **outside** the core solution (`samples/` directory) so it does not bloat `Blaze.LlmGateway.slnx`. It has its own solution or is run directly from the `samples/` subfolder.

**Policy hook.** Copilot CLI clients authenticate against the gateway with a distinct API key, which is subject to the cloud-escalation allow-list from ADR-0008. Typical configuration: the Copilot CLI key is allowed to escalate to GitHub Models and Azure Foundry, but not to OpenRouter.

**What is deferred.**

- **Copilot plugin/manifest** (`copilot-plugin.json`, marketplace submission) — requires product-side decisions on branding, ownership, and maintenance commitments.
- **MCP packaging for Copilot** — Blaze exposing its own tools to Copilot as an MCP server. Deferred because (a) the tool plane's `HostedMcpServerTool` mapping is itself a Phase-1 item per the PRD, and (b) publishing an MCP surface externally interacts with cloud-escalation policy (ADR-0008).
- **Entra / service-principal auth** — ties to the Phase-5 auth/authz follow-on ADR.

## Consequences

**Positive**

- Delivers visible Copilot compatibility in Phase 1 without blocking on ecosystem processes (plugin review, marketplace).
- Keeps Phase-1 scope focused on the platform, not on Copilot-specific packaging.
- The SDK sample doubles as a regression test for the Chat Completions surface (ADR-0003).

**Negative**

- No plugin marketplace presence in Phase 1. Users must configure Blaze manually. Acceptable for internal LAN deployments.
- If the Copilot SDK introduces a breaking change (as it did between protocol v2 and v3), the sample needs maintenance.

**Neutral**

- Adds one new `samples/` directory. Solution file unchanged.

## Alternatives Considered

### Alternative A — Ship plugin packaging in Phase 1

Full marketplace plugin from day one. **Rejected** — plugin approval cycles and branding decisions are not on the Phase-1 critical path; the value is in compatibility, not packaging.

### Alternative B — Copilot SDK only, skip BYOM docs

Build only the SDK sample. **Rejected** — BYOM compatibility is what makes Blaze immediately useful to internal developers. It is also nearly free once ADR-0003 is done; skipping docs would leave the feature undiscoverable.

### Alternative C — Skip the sample, document BYOM only

**Rejected** — the SDK sample surfaces real issues (streaming edge cases, tool-call round-trip) that BYOM smoke tests miss. Keeping it in Phase 1 gives us a continuous compatibility check.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §7 Integration plane
- [../../research/github-copilot-sdk.md](../../research/github-copilot-sdk.md) — Copilot SDK architecture and hooks
- [GitHub Copilot CLI docs](https://docs.github.com/en/copilot/github-copilot-in-the-cli)
