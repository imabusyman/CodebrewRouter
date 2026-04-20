---
name: autonomous-agent-harness
description: Provide a task queue, scheduler, and persistent memory system for autonomous multi-agent loops. Track task state, monitor agent progress, retry on failure, and persist decisions across sessions. Use this in the Orchestrator parallel path when running an autonomous PRD-driven loop without human gates.
---

# Autonomous Agent Harness — task queue + scheduler + memory

A runtime framework for fully autonomous agent workflows (no human gates). Manages task queues, agent scheduling, persistent decision logs, and failure recovery.

## When to use

- **Parallel path:** Orchestrator is running autonomously against a PRD (no human `/agent squad` gate).
- You need **task queue management** (prioritize, retry, skip on failure).
- Agent decisions must **persist across sessions** (in case of restart/recovery).
- You want **monitoring dashboards** (task status, agent health, completion %).

## Components

### 1. Task queue

Initial state (from PRD):

```
Queue:
  [pending] Task 1: Implement Provider X (assigned: Coder)
  [pending] Task 2: Unit test Provider X (assigned: Tester)
  [pending] Task 3: Wire Provider X in AppHost (assigned: Infra)
```

State transitions:

```
pending → assigned → in_progress → done / blocked / failed → (retry or skip)
```

### 2. Scheduler

- **Pull tasks** from queue in priority order (dependencies first).
- **Assign agents** (Coder for impl, Tester for tests, Infra for config).
- **Dispatch handoffs** (emit structured envelope; agent works independently).
- **Monitor for completion** (poll for `[DONE]` or `[BLOCKED]`).
- **Advance or backtrack** (move to next task or fail-retry).

### 3. Persistent memory (reasoning log)

Append-only decision log under `Docs/squad/runs/<ts>/`:

```markdown
## 2026-04-20T15:45:00Z — scheduler — MEDIUM
Decision: Task 2 (tests) was skipped due to missing implementation.
Rationale: Quality gate requires tests to run after impl completes.
Evidence: Task 1 still in [in_progress]; Task 2 moved to [queued].
```

Recovery across sessions: On restart, read the log, resume from the last checkpoint.

### 4. Failure recovery

- **Failed task:** Move to `[blocked]` + emit alert. Orchestrator decides: retry, skip, or abort.
- **Failed quality gate:** Stay in `[in_progress]`, loop back to agent with fixes needed.
- **Persist state:** If the session dies, recovery reads the log and resumes exactly where it was.

## Orchestrator integration

The Orchestrator (Conductor's parallel-path sibling) drives the harness:

1. Load PRD → generate task queue.
2. **Loop:**
   - Pull next ready task from queue.
   - Dispatch to appropriate agent (Coder / Tester / Infra).
   - Await `[DONE]` or `[BLOCKED]`.
   - Update queue + reasoning log.
   - Retry or move to next task.
3. When queue is empty + all tasks `[done]`, trigger Reviewer + Security-Review.

## Configuration

```yaml
harness:
  max_retries: 3
  task_timeout: 1h  # per task
  session_timeout: 8h  # total runtime before auto-checkpoint
  priority_rules:
    - "Architect tasks (ADRs) run first"
    - "Coder tasks (impl) run before Tester"
    - "Infra tasks (config) run last"
  failure_mode: "retry_once_then_block"  # or "fail_fast"
```

## See also

- `loop-operator` — supervise the autonomous loop, detect stalls, safe interventions.
- `subagent-driven-development` — structure subagent delegation.
- `dispatching-parallel-agents` — compute task sets from plan.
