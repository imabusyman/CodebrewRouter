---
name: team-builder
description: Compose parallel teams of specialized agents for a multi-faceted task. Map subtasks to agent roles, balance workload, and validate team composition. Use this when breaking a task into parallel streams that require different expertise (Coder, Tester, Infra, etc.).
---

# Team Builder — compose parallel agent teams

Decomposes complex tasks into parallel streams and assigns each stream to the right specialist.

## When to use

- A task naturally splits into independent subtasks (e.g., API changes + tests + deployment wiring).
- You have multiple specialist roles available (Coder, Tester, Infra, Architect).
- Subtasks have non-overlapping file ownership and can run in parallel.

## Workflow

### 1. Decompose the task

List all subtasks and identify dependencies:

```
Task: Add a new provider
  ├─ Subtask 1: Core provider implementation (Coder)
  ├─ Subtask 2: Unit + integration tests (Tester)
  ├─ Subtask 3: Aspire AppHost wiring (Infra)
  └─ Subtask 4: ADR + design notes (Architect)

Dependencies: Coder → Tester → Infra (sequential)
              Architect ∥ Coder (parallel)
```

### 2. Map to roles & estimate

Assign each subtask to the best-fit specialist:

| Subtask | Role | Files | Est. effort |
|---|---|---|---|
| Core provider | Coder | `Infrastructure/Providers/**` | M |
| Tests | Tester | `Tests/**` | M |
| AppHost | Infra | `AppHost/**, ServiceDefaults/**` | S |
| ADR | Architect | `Docs/design/adr/**` | S |

### 3. Check for conflicts

- **No shared files** across parallel subtasks (each agent owns a disjoint set).
- **Dependencies are explicit** (e.g., "Tester waits for Coder").

### 4. Create delegation handoffs

Per-agent handoff with:
- Files you may edit (exclusive lock).
- Artifacts to re-read (spec, plan, prior ADRs).
- Pending decisions (from prior phases).

## Team composition rules

- **Max 4 agents in parallel** (Conductor can manage).
- **One role per phase** (Architect before Coder before Tester before Reviewer).
- **File locks are disjoint** (no two agents editing the same file).
- **Clear dependencies** (if B depends on A, state it in B's handoff).

## See also

- `dispatching-parallel-agents` — automate agent assignment.
- `claude-devfleet` — use Git worktrees for isolated workspaces.
- `quality-gate` — ensure parallel builds pass `-warnaserror` + 95% coverage before merge.
