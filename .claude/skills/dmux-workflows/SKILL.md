---
name: dmux-workflows
description: Orchestrate parallel agent workflows using tmux pane management (via dmux or native tmux). Spin multiple agents in isolated tmux panes, monitor logs, synchronize completion, and collect results. Use this in the Orchestrator parallel path when targeting Unix/Linux or WSL environments for shell-based coordination.
---

# dmux Workflows — parallel orchestration via tmux

Use tmux pane multiplexing for true parallel execution with integrated monitoring and log aggregation.

## Prerequisites

- **Windows:** WSL 2 with Ubuntu + tmux installed, OR Git Bash / Cygwin with tmux.
- **macOS / Linux:** Native tmux (built-in or `brew install tmux`).
- **Blaze.LlmGateway:** Must be cloned into the WSL filesystem (not `/mnt/c/`, to avoid path-length issues).

## When to use

- **Orchestrator parallel path** on Unix/Linux or WSL.
- You want **integrated tmux monitoring** (watch all agents in real time).
- You need **true shell-based concurrency** (vs. task queue abstraction).
- Agents write to separate **log files** and report completion via exit codes.

## Architecture

```
tmux session: blaze-squad
├─ pane:0  [Conductor/Orchestrator] — orchestration logic
├─ pane:1  [Coder subagent 1]       — implement Provider X
├─ pane:2  [Coder subagent 2]       — implement Provider Y
├─ pane:3  [Tester subagent 1]      — test all providers
└─ pane:4  [Infra subagent 1]       — wire AppHost
```

All panes are visible; coordinator watches logs in real time.

## Workflow

### 1. Initialize tmux session

```bash
# From repo root
tmux new-session -d -s blaze-squad -x 240 -y 60

# Create panes for each agent
tmux new-window -t blaze-squad -n conductor
tmux split-window -t blaze-squad -h  # splits conductor pane horizontally
tmux split-window -t blaze-squad -v  # splits vertically into more panes
```

### 2. Dispatch agents to panes

```bash
# Pane 0: Orchestrator runs the coordination loop
tmux send-keys -t blaze-squad:0 "pwsh ./scripts/orchestrator.ps1 --prd Docs/PRD/new-feature.md" Enter

# Pane 1: Coder agent on worktree A
tmux send-keys -t blaze-squad:1 "cd .worktrees/task-1 && coder-agent --task 'implement Provider X'" Enter

# Pane 2: Coder agent on worktree B
tmux send-keys -t blaze-squad:2 "cd .worktrees/task-2 && coder-agent --task 'implement Provider Y'" Enter

# ... etc for Tester, Infra
```

### 3. Monitor & aggregate

Conductor polls all panes for completion:

```bash
# Check exit code of pane 1 (blocks until done)
tmux capture-pane -t blaze-squad:1 -p
if [ $? -eq 0 ]; then echo "Pane 1 done"; fi

# Collect logs from all panes
for pane in 1 2 3 4; do
  echo "=== Pane $pane ===" >> aggregated.log
  tmux capture-pane -t blaze-squad:$pane -p >> aggregated.log
done
```

### 4. Cleanup

```bash
tmux kill-session -t blaze-squad
```

## Agent handoff format (tmux-aware)

Each agent's handoff includes:

```yaml
execution_env: tmux
tmux_pane: blaze-squad:1
log_file: Docs/squad/runs/<ts>/logs/pane-1.log
ready_signal: "echo AGENT_DONE >> $log_file && exit 0"
error_signal: "echo AGENT_BLOCKED >> $log_file && exit 1"
```

Agent logs exit code or a status string; Orchestrator reads it from the tmux pane.

## Advantages

- **Real-time visibility:** All agents' output visible in one tmux window.
- **No polling:** tmux captures full stdout/stderr history.
- **Cleanup:** Kill the session to terminate all panes at once.

## Limitations

- **Windows:** Requires WSL (or Git Bash/Cygwin with tmux).
- **Path lengths:** `.worktrees/` must be short (WSL+Windows 260-char limit still applies).
- **Log parsing:** Agents must emit clear completion signals (exit codes or magic strings).

## See also

- `autonomous-agent-harness` — cross-platform task queue alternative (no tmux).
- `subagent-driven-development` — structure subagent delegation.
