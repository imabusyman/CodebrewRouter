# ADR-0009: Squad orchestration — dual-target Claude-powered development squad

- **Status:** Proposed
- **Date:** 2026-04-19
- **Deciders:** Architecture, Product, Developer Experience
- **Related:** ADR-0001, ADR-0006, ADR-0007, ADR-0008, [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Phase 3 - Agent integration layer", [plan/squad-orchestration-plan.md](../../plan/squad-orchestration-plan.md)

## Context

The repository already ships four stand-alone Copilot-CLI-scoped agents under `.github/agents/` (`llm-gateway-architect`, `router`, `local-ai-setup`, `local`) and well-maintained `CLAUDE.md` + `.github/copilot-instructions.md` files. But the agents are specialists with no orchestrator, no structured handoff, no clean-context review, and nothing Claude Code's `.claude/agents/` consumer can read. Gaps explicitly called out in `CLAUDE.md` §"Known Incomplete Areas" (circuit breaker, streaming failover, MCP `HostedMcpServerTool` mapping, Blazor wiring) are real work items that would benefit from an orchestrated multi-agent workflow — yet the master plan Phase 3 ("Agent integration layer") is about *runtime* agent adapters, not *development-time* agent orchestration. Burke Holland's Ultralight Orchestration pattern (research: `burkeholland-ultralight-orchestration.md`) and Microsoft's DevSquad Copilot (research: `https-github-com-microsoft-devsquad-copilot.md`) both demonstrate mature prompt-only multi-agent architectures that could fill this gap without growing the .NET codebase. ADR-0008's default-deny cloud-egress policy forces every dev-time agent to be usable on local/Claude routing, so the roster must stay lean and Claude-only. Claude Code (`.claude/agents/`) and GitHub Copilot CLI (`.github/agents/`, `.github/plugins/`) are the two consumer surfaces we already use daily; a single source-of-truth layout that materializes to both — with identical Claude model ids — gives parity without duplication.

## Decision

We will adopt an **8-agent Claude-powered development squad** authored once under `prompts/squad/`, materialized to `.github/plugins/squad/` and `.claude/` by a manual PowerShell sync script (`scripts/sync-squad.ps1`). The squad uses DevSquad's structured-action protocol (`[CONDUCTOR]` + `[ASK]`, `[CREATE]`, `[EDIT]`, `[CHECKPOINT]`, `[BLOCKED]`, `[DONE]` tags) with Ultralight's phase-based file-parallelism. Per-run artifacts live under `Docs/squad/runs/<ISO-ts>/` and are the authoritative source for clean-context review.

### Details

**Roster.** Eight roles, all Claude models (Copilot CLI accepts bare model ids without `github-copilot/` prefix for Claude models, per existing `.github/agents/llm-gateway-architect.agent.md` precedent):

| Role | Model | Purpose |
|---|---|---|
| Squad Conductor | `claude-opus-4.7` | Phase decomposition, handoff envelopes, reasoning log; never codes |
| Squad Planner | `claude-opus-4.7` | Research, `spec.md`, ordered steps with file assignments |
| Squad Architect | `claude-opus-4.7` | ADR authoring; absorbs existing `llm-gateway-architect` |
| Squad Coder | `claude-sonnet-4.6` | MEAI-compliant C# implementation |
| Squad Tester | `claude-sonnet-4.6` | xUnit + Moq + SSE integration tests; 95% coverage |
| Squad Reviewer | `claude-sonnet-4.6` | Clean-context diff review; `-warnaserror` + coverage gate |
| Squad Infra | `claude-haiku-4.5` | AppHost, Aspire, secrets; absorbs existing `local-ai-setup` |
| Squad Security-Review | `claude-opus-4.7` | ADR-0008 egress guard; auto-triggered on sensitive diffs |

**Layout.**

```
prompts/squad/                              (source of truth)
  conductor.prompt.md ... security-review.prompt.md
  _shared/*.instructions.md                 (path-scoped guardrails)
  protocol/*.md                             (action-tag vocabulary, envelope + log schemas)
  commands/*.md                             (slash-command bodies)
  README.md                                 (authoring rules)

scripts/sync-squad.ps1                      (manual generator)

.github/
  plugin/plugin.json                        (Copilot CLI plugin manifest v0.1.0)
  plugins/squad/
    .mcp.json                               (microsoft-learn + context7 + github)
    agents/   skills/   instructions/       (GENERATED)

.claude/
  agents/   skills/   commands/             (GENERATED)

Docs/
  plan/squad-orchestration-plan.md          (tactical plan)
  squad/runs/                               (per-run artifacts)
```

**Protocol.** Conductor prefixes every delegation with `[CONDUCTOR]`. Specialists respond with one of `[ASK]`, `[CREATE]`, `[EDIT]`, `[CHECKPOINT]`, `[BLOCKED]`, `[DONE]`. Disk artifacts under `Docs/squad/runs/<ts>/` (`spec.md`, `plan.md`, `handoff/NN-from-to-to.md`, `reasoning.log.md`, `review/`, `security/`) are authoritative. Reviewer and Security-Review reread from disk with clean context rather than inheriting chat.

**Phases.** (0) Research — Planner. (1) Architecture gate — Architect, only if pipeline/routing/config touched. (2..N) Implementation — Coder (or Infra for AppHost steps), phase-grouped by non-overlapping file sets for parallelism. (N+1) Testing — Tester fan-out. (N+2) Review — Reviewer, clean-context, `-warnaserror` + coverage gate. (N+3) Security gate — Security-Review, auto-triggered when diff touches Infrastructure, any `Program.cs`, `LlmGatewayOptions.cs`, `appsettings*.json`, or AppHost. (N+4) Report.

**MCP bundle.** `.github/plugins/squad/.mcp.json` ships three servers: `microsoft-learn` (already in repo), `context7` (Ultralight requires Coder to consult for live docs), `github` (Reviewer cross-checks commit history). Security-Review remains read-only.

**Guardrails.** Path-scoped `.instructions.md` under `_shared/` enforce: MEAI law (Infrastructure/**, Api/**, Core/**), Aspire secrets handling (AppHost/**, ServiceDefaults/**), test conventions (Tests/**, Benchmarks/**), ADR format (`Docs/design/adr/**`), ADR-0008 cloud egress (**/*.cs, appsettings*.json), code style (**/*.cs).

**Sync mechanics.** `scripts/sync-squad.ps1` parses YAML frontmatter in each `prompts/squad/<role>.prompt.md` and emits two variants: Copilot-CLI-flavored under `.github/plugins/squad/agents/squad.<role>.agent.md` (tools: `read`, `edit`, `search`, `shell`, `github`, `web`, `agent`) and Claude-Code-flavored under `.claude/agents/squad-<role>.md` (tools: `Read`, `Edit`, `Grep`, `Glob`, `Bash`, `WebFetch`, `Agent`). Model ids are bare Claude names in both targets. The script is idempotent, runs on demand, and is not CI-enforced — drift is caught at PR review time.

**Agent absorption.** The existing `llm-gateway-architect.agent.md` body becomes `prompts/squad/architect.prompt.md` (with corrections: `GetResponseAsync`/`GetStreamingResponseAsync` replace `CompleteAsync`/`CompleteStreamingAsync`, SDK table expanded from 5 to all 9 providers). `local-ai-setup.agent.md` body becomes `prompts/squad/infra.prompt.md`. The original files are replaced with 10-line deprecation pointers. `router.agent.md` is renamed to `model-router.agent.md` to free the "router" noun for task orchestration; `local.agent.md` (Ollama on-device assistant) is left alone.

**Invocation ergonomics.** Copilot CLI: `copilot plugin install ./.github/plugins/squad` then `/agent squad`. Claude Code: `.claude/commands/squad-plan.md` (and `squad-implement`, `squad-review`, `squad-security`) provide slash-command entry.

## Consequences

**Positive**

- Artifact traceability — every squad run leaves a reasoning log and handoff envelope trail on disk.
- Clean-context review enforced by disk-authoritative artifacts; Reviewer cannot inherit compromised context.
- Dual-target parity — identical agent bodies serve Copilot CLI and Claude Code via one sync step.
- Zero .NET code growth — the squad is markdown + JSON + one PowerShell script.
- Absorbs (rather than duplicates) the existing `llm-gateway-architect` and `local-ai-setup` agents with upgraded models.
- Honors ADR-0008 egress policy by staffing every agent with Claude models and including a dedicated Security-Review gate.

**Negative**

- Manual `sync-squad.ps1` can drift if a contributor edits generated files directly. Mitigation: `prompts/squad/README.md` documents the rule; PR reviewers check.
- Three MCP servers (microsoft-learn + context7 + github) to keep healthy; an external MCP failure degrades squad capability.
- Adds ~40 markdown files under `prompts/squad/`, `.github/plugins/squad/`, and `.claude/`. Increased doc surface area.

**Neutral**

- Renames `.github/agents/router.agent.md` → `model-router.agent.md`. One-line update to `.github/copilot-instructions.md`.
- Root `CLAUDE.md` gains a small "Squad Guardrails" pointer block; no other changes.
- `.claude/settings.local.json` gains read-only permissions (`Bash(dotnet:*)`, `Bash(git status|diff|log:*)`, `WebFetch`).

## Alternatives Considered

### Alternative A — Symlinks from `.github/` / `.claude/` to a single shared source

Use OS symlinks so the two target dirs are aliases of one physical location. **Rejected** because (a) Windows symlinks require developer-mode / admin and git treats them inconsistently, (b) Copilot CLI and Claude Code want *different* frontmatter (tool-name casing and, historically, model-id prefixes), so a pure link would break at least one target.

### Alternative B — Single-variant file with dual frontmatter blocks

Embed two frontmatter blocks and let each CLI pick its own. **Rejected** — neither Copilot CLI nor Claude Code support this. YAML frontmatter is singular and positional.

### Alternative C — Absorb the runtime `router.agent.md` into the squad as a ninth specialist

Fold the existing model-router (request-time classifier) into the squad. **Rejected** — the runtime router operates at request time inside Copilot CLI and emits a deterministic `INTENT/COMPLEXITY/ROUTE/FALLBACK` block, not a task workflow. Conflating it with the squad's Conductor (development-time orchestrator) blurs two orthogonal concerns.

### Alternative D — CI-enforced sync

Add `.github/workflows/squad-sync-check.yml` that runs `sync-squad.ps1` and diffs against checked-in output, failing PRs on drift. **Rejected for v1** — the current repo style is low-ceremony; a PR-level reviewer check is sufficient. Upgradable later without schema change.

### Alternative E — Full DevSquad port (13 agents + nested workers)

Adopt Microsoft DevSquad's full 13-agent hierarchy with nested plan/implement/review/refine workers. **Rejected for v1** — too heavy for a 1-person / small-team repo; the cognitive overhead of 13 roles outweighs the per-run isolation benefit. The 8-role lean squad captures 80% of the value. Upgradable per future ADR.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §6 Agent plane
- [../../plan/squad-orchestration-plan.md](../../plan/squad-orchestration-plan.md) — tactical implementation plan
- [../../research/burkeholland-ultralight-orchestration.md](../../research/burkeholland-ultralight-orchestration.md) — Burke Holland Ultralight pattern
- [../../research/https-github-com-microsoft-devsquad-copilot.md](../../research/https-github-com-microsoft-devsquad-copilot.md) — Microsoft DevSquad format
- [../../research/everything-claude-code-deep-dive.md](../../research/everything-claude-code-deep-dive.md) — Claude Code conventions (agents, skills, commands, hooks)
- [../../research/github-awesome-copilot.md](../../research/github-awesome-copilot.md) — Copilot plugin marketplace conventions
- [ADR-0001](./0001-primary-host-boundary.md), [ADR-0006](./0006-azure-foundry-agents-integration.md), [ADR-0007](./0007-copilot-ecosystem-strategy.md), [ADR-0008](./0008-cloud-escalation-policy.md)
