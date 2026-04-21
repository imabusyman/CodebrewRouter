---
description: |
  Orchestrate an autonomous, parallel development workflow driven by a Product Requirements Document (PRD).
  The Orchestrator spins isolated Git worktrees, dispatches Coder/Tester/Infra subagents in parallel, monitors progress, and aggregates results.
  Use this when you want a fully autonomous PRD-driven loop (no human gates between phases).
  Compare: `/agent squad` for human-gated phased development; `/orchestrate` for autonomous parallel loop.
argument-hint: |
  --prd <path-to-prd.md>     Load an existing PRD file.
  or
  <raw-task-description>     Describe the task; Orchestrator will generate a PRD first.
---

# /orchestrate — autonomous parallel development loop

Kicks off the Orchestrator agent in autonomous mode, reading a PRD (or generating one) and decomposing into parallel implementation tasks.

## When to use

- You have a complete task description (PRD or raw text) and want **fully autonomous execution**.
- The task naturally decomposes into **parallel, non-overlapping streams** (Coder, Tester, Infra).
- You are **comfortable with no human gates** between phases (vs. `/agent squad` which pauses between phases for review).
- You want a **clean-context reasoning log** captured for audit afterward (just like the Conductor).

## When NOT to use

- The task is exploratory or high-risk → use `/agent squad` (human-gated Conductor).
- You need immediate mid-phase feedback or changes → use `/agent squad`.
- The PRD is unclear or incomplete → start with `/agent write-a-prd` to clarify first.

## Invocation

```powershell
# Option 1: Load a pre-written PRD
/orchestrate --prd Docs/PRD/my-feature.md

# Option 2: Provide a task description (Orchestrator generates PRD)
/orchestrate "Add Circuit Breaker to LlmRoutingChatClient for timeout resilience"
```

## What happens

1. **PRD Loading/Generation:** If `--prd` provided, read it. Otherwise, invoke `write-a-prd` skill.
2. **Decomposition:** Break the PRD into parallel tasks (Coder, Tester, Infra, Architect).
3. **Worktree Setup:** Create isolated Git worktrees for each parallel task.
4. **Dispatch:** Emit handoffs; spawn subagent instances (Coder, Tester, Infra).
5. **Monitor Loop:** Poll for `[DONE]` signals. Retry on `[BLOCKED]`. Log every decision.
6. **Quality Gate:** Merge all branches, run `-warnaserror` + 95% coverage gate.
7. **Reviewer & Security:** Trigger existing specialist agents on the final diff.
8. **Artifact Location:** All runs stored under `Docs/squad/runs/<ts>-<slug>/`.

## Invariants

- **Parallel, not concurrent:** Tasks do not share files (enforced by file-lock disjointness check).
- **Quality gate mandatory:** Every merge must pass `-warnaserror` + 95% coverage.
- **Reasoning log:** All decisions logged to `reasoning.log.md` (audit trail).
- **Structured-action tags:** `[DONE]`, `[BLOCKED]`, `[CHECKPOINT]` throughout.
- **Clean-context review:** Can be reviewed after completion (like the Conductor).

## Comparison: `/agent squad` vs `/orchestrate`

| Aspect | `/agent squad` (Conductor) | `/orchestrate` (Orchestrator) |
|---|---|---|
| **Gating** | Human gate between phases | Fully autonomous |
| **Parallelism** | Sequential phases | Parallel tasks (within phases) |
| **Execution** | Phased + deterministic | Loop-driven + adaptive |
| **Review** | At every phase boundary | At the end (full reasoning log) |
| **Risk** | Lower (human feedback) | Higher (no human gates) |
| **Speed** | Slower (human gates) | Faster (no waits) |
| **Artifacts** | `Docs/squad/runs/<ts>/` | `Docs/squad/runs/<ts>/` (same) |

## Success criteria

After `/orchestrate` completes:

- [ ] `Docs/squad/runs/<ts>-<slug>/prd.md` — PRD (canonical or generated).
- [ ] `Docs/squad/runs/<ts>-<slug>/spec.md` — detailed specification.
- [ ] `Docs/squad/runs/<ts>-<slug>/plan.md` — decomposed task plan.
- [ ] `Docs/squad/runs/<ts>-<slug>/reasoning.log.md` — decision log.
- [ ] `Docs/squad/runs/<ts>-<slug>/handoff/` — per-agent handoff envelopes.
- [ ] Git main branch — all worktree branches merged, quality-gate passed.
- [ ] `.worktrees/` — cleaned up (or preserved for audit).

## Common issues

### PRD is incomplete
Invoke `/agent write-a-prd` first to clarify. Then call `/orchestrate --prd <path>`.

### A subagent stalls
The `loop-operator` skill will retry once. If it fails twice, the loop halts and logs `[BLOCKED]`. Check `reasoning.log.md` for details.

### Quality gate fails
Orchestrator will not merge. Loop back with fixes (Coder re-attempts). Check build output for `-warnaserror` and coverage gaps.

### Mid-run intervention
Stop the orchestration and switch to `/agent squad` (human-gated Conductor). The run directory is preserved; you can resume manually.

## See also

- `/agent squad` — phased, human-gated development (Conductor).
- `/agent write-a-prd` — generate a PRD before orchestrating.
- **Skills:** `autonomous-agent-harness`, `loop-operator`, `dispatching-parallel-agents`, `claude-devfleet`.
- **ADR-0010** — architectural rationale for parallel-path coexistence.
