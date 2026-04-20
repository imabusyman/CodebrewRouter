---
name: claude-devfleet
description: Orchestrate parallel agent development using Git worktrees. Spin isolated checkouts for each task, assign Coders to independent worktrees, monitor progress, and merge results back. Use this when the Conductor or Orchestrator needs to parallelize file-disjoint implementation tasks across multiple agents.
---

# Claude DevFleet — parallel development via Git worktrees

Enables multi-agent parallel coding without file conflicts. Each agent gets its own worktree on an isolated branch.

## When to use

- Multiple Coder agents are assigned non-overlapping file sets in the same phase.
- You need fully isolated development trees (no shared working directory state).
- Results must be mergeable back to the main branch without manual conflict resolution.

## Workflow

### Setup

```powershell
# From repo root; create a worktree for each parallel task
git worktree add -b feature-name .worktrees/<task-slug> main
```

### Assignment

Each Coder agent receives an exclusive worktree path in their handoff envelope:

```yaml
files you may edit (exclusive lock):
  - .worktrees/<task-slug>/**  (create | edit)
```

### Execution

Each agent works independently in their worktree. No coordination needed until merge time.

### Cleanup & merge

1. Each agent commits their work to their isolated branch.
2. Orchestrator (or Conductor) merges branches back to main: `git merge feature-name`.
3. Remove the worktree: `git worktree remove .worktrees/<task-slug>`.

## Requirements

- Git 2.7.0+ (built-in `git worktree` support).
- `.worktrees/` directory must be git-ignored (entry in `.gitignore`).
- Ensure no two agents are assigned the same worktree or branch.

## Pitfalls

- **Shared branch in multiple worktrees:** Git will error. Assign each worktree a unique branch name.
- **Long paths:** Windows has a 260-char path limit. Keep task slugs short (5 words max).
- **Stale branches:** After merge, delete the branch: `git branch -d feature-name`.

## See also

- `dispatching-parallel-agents` — assign Coder agents to worktrees automatically.
- `team-builder` — compose parallel agent teams.
