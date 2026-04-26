# JARVIS Fleet — Source of Truth

This directory is the **single source of truth** for the JARVIS roadmap-execution fleet. It is the parallel of [`prompts/squad/`](../squad/README.md): a set of agent prompts that materialize into both Claude Code agents (`.claude/agents/`) and a GitHub Copilot CLI plugin (`.github/plugins/jarvis/`).

The JARVIS fleet is purpose-built for the 9-phase roadmap in [`analysis.md`](../../analysis.md) — the personal-developer-agent / Yardly-vision plan that sits *above* the Squad. Squad fixes a specific code task end-to-end. JARVIS picks the next phase, decides which specialist to dispatch (often a Squad agent for implementation), and tracks roadmap progress.

## Roster

| Source file | Claude file | Copilot file | Phase(s) |
|---|---|---|---|
| `conductor.prompt.md` | `.claude/agents/jarvis-conductor.md` | `jarvis.conductor.agent.md` | Orchestrates all phases |
| `gateway-bugfix.prompt.md` | `.claude/agents/gateway-bugfix.md` | `jarvis.gateway-bugfix.agent.md` | 1 — bug-fix |
| `memory-architect.prompt.md` | `.claude/agents/jarvis-memory-architect.md` | `jarvis.memory-architect.agent.md` | 2, 4 — sessions + RAG |
| `tools-architect.prompt.md` | `.claude/agents/jarvis-tools-architect.md` | `jarvis.tools-architect.agent.md` | 3 — MCP + tools |
| `agent-architect.prompt.md` | `.claude/agents/jarvis-agent-architect.md` | `jarvis.agent-architect.agent.md` | 5, 6 — runtime + persona |
| `vision-architect.prompt.md` | `.claude/agents/jarvis-vision-architect.md` | `jarvis.vision-architect.agent.md` | 8 — vision passthrough |

The `gateway-bugfix` agent intentionally drops the `jarvis-` prefix because it is a tightly-scoped Phase-1 worker rather than a JARVIS-layer architect.

## Editing rules

1. **Edit only files in this directory.** Never edit the generated copies under `.claude/agents/` or `.github/plugins/jarvis/` by hand — they get clobbered on next sync.
2. **Keep frontmatter Claude-flavored.** Tools listed as `[Read, Edit, Grep, Glob, Bash, WebFetch]`. The sync script translates to Copilot CLI vocabulary (`read, edit, search, shell, web`).
3. **After every edit, run** `pwsh ./scripts/sync-jarvis.ps1` from the repo root.
4. **Commit the diff.** Sync drift is caught at PR review, not by CI.

## Invoking

### Claude Code

```
> Use the jarvis-conductor agent.
```

The conductor reads `analysis.md`, picks the next phase, dispatches a specialist.

### Copilot CLI

Install once:

```pwsh
copilot plugin install ./.github/plugins/jarvis
```

Invoke:

```pwsh
copilot
> /agent jarvis
```

Or jump straight to a specialist:

```pwsh
> /agent jarvis.gateway-bugfix
> /agent jarvis.memory-architect
```

## See also

- [`analysis.md`](../../analysis.md) — the roadmap the conductor reads.
- [`prompts/squad/`](../squad/) — the underlying 9-agent code-work fleet.
- [`scripts/sync-jarvis.ps1`](../../scripts/sync-jarvis.ps1) — materialization script.
- [ADR-0009](../../Docs/design/adr/0009-squad-orchestration.md) — orchestration model the JARVIS fleet inherits.
