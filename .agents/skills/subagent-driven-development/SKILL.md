---
name: subagent-driven-development
description: "Delegate implementation work to specialized subagent instances, each inheriting a task scope and constraints from the parent. Emit structured handoffs, monitor completion signals, aggregate results. Use this in the Orchestrator parallel path when spawning independent Coder/Tester/Infra subagents from a single coordinator."
---

# Subagent-Driven Development â€” hierarchical delegation

Structures multi-agent development as a hierarchy: parent Orchestrator spawns child subagents, each with a bounded scope.

## When to use

- A single Orchestrator needs to farm work to multiple Coder subagents (e.g., parallel worktrees).
- Each subagent must be **isolated** and **stateless** (no cross-agent chat context).
- Results must be **aggregatable** back to the parent (clean merge).

## Architecture

```
Orchestrator (parent)
  â”œâ”€ Coder subagent 1 (worktree A, task scope A)
  â”œâ”€ Coder subagent 2 (worktree B, task scope B)
  â”œâ”€ Tester subagent 1 (tests for A + B)
  â””â”€ Infra subagent 1 (wire A + B into AppHost)
```

Each subagent receives:
- **Isolated task scope** (handoff envelope with exclusive file-lock).
- **Read-only artifacts** (spec, plan, prior commits).
- **Completion signal** (emit `[DONE]` tag when finished).

## Handoff protocol

Parent â†’ subagent:

```yaml
---
from: orchestrator
to: coder-subagent-1
task: implementation.provider-wiring
files you may edit (exclusive lock):
  - Blaze.LlmGateway.Infrastructure/Providers/NewProvider.cs (create)
  - .worktrees/add-new-provider/**  (working directory)
artifacts to re-read:
  - Docs/squad/runs/<ts>/spec.md
  - Docs/squad/runs/<ts>/plan.md
inherited assumptions:
  - Provider must follow MEAI IChatClient pattern (see AGENTS.md).
  - Quality gate: -warnaserror + 95% coverage.
pending decisions:
  - (none)
---
```

Subagent â†’ parent:

```
[DONE]
Completed NewProvider.cs + 8 unit tests (100% coverage).
Committed to branch 'feature/add-new-provider'.
Ready for merge.
```

## Aggregation

After all subagents complete `[DONE]`:

1. **Quality gate:** Merge each subagent's branch, run `-warnaserror` + coverage.
2. **Integrate:** Combine all changes into a single coherent state.
3. **Test end-to-end:** Run full test suite on merged main.
4. **Advance phase:** Move to Reviewer or Security-Review.

## Best practices

- **One subagent per file set** (avoid coordination).
- **Subagents are stateless** (read handoff, work, emit `[DONE]`, no iterative chat).
- **Parent tracks progress** (collect `[DONE]` signals; halt on `[BLOCKED]`).
- **Clear ownership** (each file belongs to exactly one subagent in a phase).

## See also

- `autonomous-agent-harness` â€” task queue + monitoring for subagents.
- `dispatching-parallel-agents` â€” compute file-disjoint task sets.
- `team-builder` â€” compose subagent teams manually.
