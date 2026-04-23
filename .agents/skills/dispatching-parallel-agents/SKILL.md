---
name: dispatching-parallel-agents
description: "Automatically dispatch multiple agent subtasks in parallel based on file-lock disjointness. Compute task dependencies, validate no file conflicts, emit parallel delegation handoffs. Use this when the Conductor or Orchestrator has multiple independent Coder/Tester tasks to farm out simultaneously."
---

# Dispatching Parallel Agents â€” automate task assignment

Manages the mechanics of launching multiple agents in parallel without manual file-conflict checking.

## When to use

- The plan lists multiple implementation steps with **non-overlapping file sets**.
- The Conductor or Orchestrator wants to **parallelize** those steps.
- You need to **validate** that parallel execution won't cause merge conflicts.

## Workflow

### Input

A plan with steps like:

```
## Step 1: Add GeminiProvider class
Files: Blaze.LlmGateway.Infrastructure/Providers/GeminiProvider.cs (create)

## Step 2: Add GeminiProvider tests
Files: Blaze.LlmGateway.Tests/Providers/GeminiProviderTests.cs (create)

## Step 3: Wire GeminiProvider in AppHost
Files: Blaze.LlmGateway.AppHost/Program.cs (edit)
```

### Analysis

1. **Extract file sets** from each step.
2. **Check for conflicts:** Do any two steps touch the same file? If yes, they must be sequential.
3. **Topologically sort:** Group non-conflicting steps into "phases."
4. **Assign to agents:** Phase 1 step 1 â†’ Coder (git worktree A), Phase 1 step 2 â†’ Tester (worktree B), etc.

### Output

For Phase 1 (parallel):

```yaml
# Handoff 1: Conductor â†’ Coder (worktree A)
files you may edit:
  - Blaze.LlmGateway.Infrastructure/Providers/GeminiProvider.cs (create)
files other parallel tasks own:
  - Blaze.LlmGateway.Tests/Providers/GeminiProviderTests.cs (Tester)
  - Blaze.LlmGateway.AppHost/Program.cs (Infra, Phase 2)
```

```yaml
# Handoff 2: Conductor â†’ Tester (worktree B)
files you may edit:
  - Blaze.LlmGateway.Tests/Providers/GeminiProviderTests.cs (create)
files other parallel tasks own:
  - Blaze.LlmGateway.Infrastructure/Providers/GeminiProvider.cs (Coder)
  - Blaze.LlmGateway.AppHost/Program.cs (Infra, Phase 2)
```

Both execute in parallel. When done, Phase 2 starts (Infra depends on both).

## Key invariants

- **No file overlap** within a phase (verified automatically).
- **Dependencies explicit** (serial phase â†’ parallel phases â†’ serial phase, etc.).
- **Each agent owns an exclusive worktree** (via `Codex-devfleet`).
- **Quality gate applies** to every parallel merge (all must pass `-warnaserror` + 95% coverage).

## See also

- `team-builder` â€” decompose tasks into parallel streams (manual).
- `Codex-devfleet` â€” Git worktree isolation (execution).
- `quality-gate` â€” validate parallel merges before advancing phases.
