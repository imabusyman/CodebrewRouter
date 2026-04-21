---
name: loop-operator
description: "Supervise an autonomous multi-agent loop, detecting stalls, managing timeouts, and performing safe interventions when agents are blocked or slow. Monitor agent health, log decisions, and escalate issues. Use this in the Orchestrator parallel path to ensure the autonomous loop makes progress and doesn't deadlock or hang."
---

# Loop Operator â€” autonomous loop supervision and recovery

A guardian process that monitors the Orchestrator's autonomous loop for stalls, timeouts, and deadlocks. Detects problems and intervenes safely.

## When to use

- **Orchestrator is running autonomously** (no human `/agent squad` gate).
- You want **health monitoring** and **automatic recovery** from stalls.
- Agent tasks may hang, timeout, or deadlock; intervention required.

## Responsibilities

### 1. Health monitoring

Periodically check:

- **Task queue progress:** Is the next task moving forward, or stuck in `[in_progress]`?
- **Agent responsiveness:** Has the agent emitted a status update in the last N minutes?
- **Resource usage:** Is memory/CPU growing unbounded?

### 2. Detection rules

**Stall detected when:**

- A task has been `[in_progress]` for > `task_timeout` (e.g., 1 hour).
- No `[CHECKPOINT]` or progress message from agent in > 30 minutes.
- Task queue is empty but end-to-end progress is incomplete.

**Deadlock detected when:**

- Multiple tasks are mutually blocked on each other's completion.
- A task depends on a predecessor, but the predecessor is stalled.

### 3. Interventions

#### Safe restart

If a task stalls:

1. **Warn:** Log `[BLOCKED]` with reason.
2. **Retry:** Reassign the task to a fresh agent instance (same role).
3. **Log:** Record the retry in reasoning log with timestamp + reason.

#### Escalation

If a task fails twice:

1. **Mark as `[blocked]`:** Remove from queue.
2. **Notify Conductor:** Log the issue and human decision is needed.
3. **Halt loop:** Pause other tasks; wait for human override.

#### Timeout enforcement

If total session > `session_timeout` (e.g., 8 hours):

1. **Checkpoint:** Save all partial work.
2. **Emit report:** Summarize progress + incomplete tasks.
3. **Graceful shutdown:** Kill remaining agents; preserve state for recovery.

### 4. Recovery from restart

When restarting an interrupted loop:

1. **Read reasoning log** from `Docs/squad/runs/<ts>/reasoning.log.md`.
2. **Find last checkpoint:** Identify which tasks completed.
3. **Resume queue:** Start with the next incomplete task.
4. **Revalidate file locks:** Ensure no two agents claim the same file.

## Configuration

```yaml
loop_operator:
  task_timeout: 1h
  checkpoint_interval: 30m
  stall_detection_interval: 5m
  max_retries: 2
  session_timeout: 8h
  escalation_notify: "conductor"  # or "user"
  heartbeat_required: true
```

## Heartbeat protocol

Each agent must emit periodic heartbeats:

```
[CHECKPOINT] Task X, step 5/10, 50% done. ETA 20 mins.
```

Loop operator reads handoff artifact's `progress` field; if no update for 30 mins â†’ stall detected.

## Decisions logged

```markdown
## 2026-04-20T16:00:00Z â€” loop-operator â€” MEDIUM
Decision: Task 1 (Coder) timed out after 1h. Reassigned to fresh Coder.
Rationale: No heartbeat for 45 mins; task was hanging on API call.
Evidence: .worktrees/task-1/logs/coder.log shows [CHECKPOINT] stopped at t=0:15.
Action: Killed agent; reset worktree; re-dispatched with timeout=30m.
```

## See also

- `autonomous-agent-harness` â€” task queue driver (Loop Operator monitors it).
- `subagent-driven-development` â€” defines agent structure that Loop Operator supervises.
