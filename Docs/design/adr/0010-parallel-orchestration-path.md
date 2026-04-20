# ADR-0010 — Parallel-Orchestration Path: Coexistence with Phased Squad

**Date:** 2026-04-20  
**Status:** Accepted  
**Authors:** Copilot  
**Relates to:** [ADR-0009](./0009-squad-orchestration.md)

## Context

ADR-0009 established an 8-agent phased development squad (Conductor) for human-gated, sequential development:
- Planner → Architect → Coder → Tester → Reviewer → Security-Review.
- Clean-context review at each phase boundary.
- Structured-action tags and reasoning logs for auditability.

However, this phased approach has limitations:
1. **Sequential:** Even non-overlapping Coder tasks run one at a time (no parallelism).
2. **Slow:** Each phase waits for human approval before advancing.
3. **Brittle:** Mid-phase feedback loops require manual restart/recovery.

For well-scoped PRDs with clear task decomposition, a fully autonomous **parallel-orchestration path** would:
- Parallelize non-overlapping tasks within phases.
- Run autonomously without human gates (faster, more responsive).
- Use the same artifact layout, reasoning log, and quality-gate invariants as the Conductor (clean-context review after completion).

## Decision

**Add a new `Orchestrator` agent alongside the existing Conductor.** Both paths coexist in the same repo, using the same squad infrastructure and invariants:

- **Phased Conductor** (`/agent squad`): Human-gated, sequential, deterministic. Use for exploration, high-risk changes, or when you want human feedback.
- **Orchestrator** (`/orchestrate`): Autonomous, parallel, PRD-driven. Use for well-scoped tasks and maximum speed.

Both emit the same artifacts:
- `Docs/squad/runs/<ts>-<slug>/`
- Structured-action tags (`[DONE]`, `[BLOCKED]`, `[CHECKPOINT]`)
- `reasoning.log.md` (append-only decision log)
- `handoff/` (per-agent scope envelopes)
- Quality-gate enforcement (both paths require `-warnaserror` + 95% coverage before merge)

## Rationale

### 1. **Preserves clean-context review invariant**
Both Conductor and Orchestrator use the same `Docs/squad/runs/<ts>-<slug>/` layout. After completion, a human can:
- Read the reasoning log to understand every decision.
- Read all handoff envelopes to trace agent scopes.
- Review the final diff with full context.

The clean-context review happens *after* autonomous execution completes, rather than between phases. This is acceptable for PRD-driven tasks where requirements are well-understood upfront.

### 2. **No file-lock conflicts**
Both paths enforce the same file-lock disjointness invariant:
- Each agent owns an exclusive file set in a phase.
- Parallel subagents (in Orchestrator) or sequential agents (in Conductor) cannot overlap.
- This ensures clean merges without manual conflict resolution.

### 3. **Same quality gate**
Both paths require:
- Build with `-warnaserror` (no warnings).
- Code coverage ≥ 95%.
- ADR-0008 cloud-egress compliance.

The quality gate is the final merge gate for both paths. No exceptions.

### 4. **Same skill ecosystem**
The 8 new coordination skills (`claude-devfleet`, `autonomous-agent-harness`, `loop-operator`, etc.) are available to both:
- Conductor: Uses them explicitly in phase planning.
- Orchestrator: Uses them natively in the autonomous loop.

Skills are not path-specific; they're shared infrastructure.

## Implementation

### Parallel-orchestration skills (Phase C–E)

| Skill | Role | Scoped to |
|---|---|---|
| `write-a-prd` | Generate PRD from task | Both paths |
| `team-builder` | Compose parallel teams (manual) | Both paths |
| `dispatching-parallel-agents` | Auto-assign parallel tasks | Both paths |
| `claude-devfleet` | Git worktree isolation | Both paths |
| `subagent-driven-development` | Hierarchical delegation | Both paths |
| `autonomous-agent-harness` | Task queue + scheduler + memory | Orchestrator only |
| `loop-operator` | Loop supervision + recovery | Orchestrator only |
| `dmux-workflows` | tmux-based orchestration (WSL/Unix) | Orchestrator only |

### New Orchestrator agent (Phase D)

- **Role:** Ralph (autonomous loop operator).
- **Scope:** `.worktrees/**`, `Docs/squad/runs/**`.
- **Model:** Claude Opus 4.7 (matches Conductor for consistency).
- **Invocation:** `/orchestrate --prd <path>` or `/orchestrate "<task-description>"`.

### New `/orchestrate` command (Phase D)

Accepts a PRD path or task description. If no PRD exists, delegates to `write-a-prd` skill first.

### Sync script extension (Phase E)

Added `orchestrator` to `$roles` array in `scripts/sync-squad.ps1`. Skills are directory-driven (no hard-coded list), so all 8 new skills automatically materialize to both `.github/plugins/squad/` and `.claude/` on every sync run.

## Invariants preserved

1. **Structured-action tags:** Both paths use `[DONE]`, `[BLOCKED]`, `[CHECKPOINT]`, etc.
2. **Reasoning log:** Append-only decision log at `Docs/squad/runs/<ts>/reasoning.log.md`.
3. **Handoff envelopes:** Per-agent scope, constraints, and assumptions in YAML.
4. **File-lock disjointness:** No two agents own the same file in a phase.
5. **Quality gate:** `-warnaserror` + 95% coverage before any merge.
6. **Clean-context review:** Reasoning log + diff can be audited after completion.
7. **Skill ecosystem:** Both paths access the same coordination skills.

## Consequences

### Positive

- **Parallelism:** Fully independent Coder tasks can run in parallel (Git worktrees isolate).
- **Speed:** Autonomous execution eliminates human-gate waits.
- **Automation:** For well-scoped PRDs, the Orchestrator requires zero human intervention until review.
- **Auditability:** Reasoning log captures every decision; clean-context review possible after completion.

### Negative

- **Complexity:** Two execution paths increase cognitive load (users must choose which path to use).
- **Risk:** Autonomous execution without mid-phase gates means mistakes compound faster (mitigated by `loop-operator` supervision and task-level retry).
- **Maintenance:** Both Conductor and Orchestrator code must be kept in sync on shared invariants (guardrails, quality gate, artifact layout).

## Alternatives considered

1. **Replace Conductor with Orchestrator** — breaks the human-gated path; unacceptable for high-risk tasks.
2. **Add parallelism only to Conductor** — complicates the Conductor's phased model; clean-context review boundaries become ambiguous.
3. **Separate worktree system** — creates duplicate artifact layout logic; violates DRY principle.

Coexistence is the best fit: users choose the right tool for the task.

## Coexistence model

Both paths are **fully coexistent:**
- Same repo.
- Same squad infrastructure (agents, skills, guardrails).
- Same artifact layout (`Docs/squad/runs/<ts>/`).
- Same quality gate (build + coverage).
- Independent invocation (no state shared between runs).

A user can switch between paths mid-task by:
1. Saving the Orchestrator's reasoning log + handoffs.
2. Invoking the Conductor on a fresh PRD (or using the same PRD as a spec).
3. The Conductor picks up from a different starting point (no collision).

## Future work

1. **ADR-0011 (planned):** Circuit breaker + mid-stream failover (applies to both paths).
2. **Orchestrator reliability:** Add timeout policies, retry budgets, and escalation rules to `loop-operator`.
3. **Conductor + Orchestrator hybrid:** Allow a task to start with the Conductor (exploration) and auto-switch to the Orchestrator (autonomous execution) once the PRD is approved.
4. **Per-run telemetry:** Metrics on task duration, agent productivity, quality-gate failure rates (applies to both paths).

## References

- [ADR-0008](./0008-cloud-egress-default-deny.md) — Cloud-egress policy (applies to both paths).
- [ADR-0009](./0009-squad-orchestration.md) — Phased squad (Conductor).
- [prompts/squad/](../../prompts/squad/) — Source of truth for all squad prompts, agents, skills, and guardrails.
- [Docs/squad/](../../Docs/squad/) — Artifact layout and lifecycle documentation.
