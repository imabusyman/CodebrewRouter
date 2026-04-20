# Squad Orchestration — Tactical Plan

Companion to [ADR-0009](../design/adr/0009-squad-orchestration.md). Ground-level task list that turns the ADR decision into concrete files, commands, and verification steps. Intended audience: contributors materializing or maintaining the squad surface.

---

## 1. Scope and non-goals

### In scope

- Create a source-of-truth authoring tree under `prompts/squad/` for 8 Claude-powered development agents.
- Emit per-target copies to `.github/plugins/squad/` (Copilot CLI) and `.claude/` (Claude Code) via a manual PowerShell sync script.
- Ship structured-action protocol, handoff-envelope schema, reasoning-log schema, path-scoped `.instructions.md` guardrails, and three shared skills (reasoning, quality-gate, prove-it-bugfix).
- Register the squad as a Copilot CLI plugin (`.github/plugin/plugin.json` + `.github/plugins/squad/.mcp.json`).
- Provide Claude Code slash commands (`.claude/commands/squad-*.md`) that match the Copilot CLI UX.
- Absorb the two legacy specialist agents (`llm-gateway-architect`, `local-ai-setup`) into the squad Architect and Infra roles. Rename the legacy `router` agent to `model-router` to free the "router" noun.
- Add a per-run artifact root at `Docs/squad/runs/` with a README describing how to drive a run.
- Update `CLAUDE.md`, `.github/copilot-instructions.md`, and `.claude/settings.local.json` so both CLIs discover the squad automatically.

### Non-goals

- No new .NET code. No changes under `Blaze.LlmGateway.*/**`.
- No modification to existing ADRs 0001–0008.
- No CI enforcement of the sync script. Drift is caught at PR review time.
- No marketplace packaging. The Copilot CLI plugin is installed locally by path.
- No replacement of the `local` agent (Ollama on-device assistant) — orthogonal to squad.

---

## 2. Implementation order

Strict ordering — each step is small, independently verifiable, and preserves a green tree.

### Step 1 — ADR

- [x] `Docs/design/adr/0009-squad-orchestration.md` (Proposed).

### Step 2 — Source-of-truth authoring tree

- [x] `prompts/squad/conductor.prompt.md`
- [x] `prompts/squad/planner.prompt.md`
- [x] `prompts/squad/architect.prompt.md` (absorbs `llm-gateway-architect` body + MEAI API corrections)
- [x] `prompts/squad/coder.prompt.md`
- [x] `prompts/squad/tester.prompt.md`
- [x] `prompts/squad/reviewer.prompt.md`
- [x] `prompts/squad/infra.prompt.md` (absorbs `local-ai-setup` body)
- [x] `prompts/squad/security-review.prompt.md`

### Step 3 — Shared guardrails and protocol

- [x] `prompts/squad/_shared/guardrails.instructions.md`
- [x] `prompts/squad/_shared/meai-infrastructure.instructions.md`
- [x] `prompts/squad/_shared/aspire-apphost.instructions.md`
- [x] `prompts/squad/_shared/tests.instructions.md`
- [x] `prompts/squad/_shared/adr.instructions.md`
- [x] `prompts/squad/_shared/cloud-egress.instructions.md`
- [x] `prompts/squad/_shared/style.instructions.md`
- [x] `prompts/squad/protocol/structured-actions.md`
- [x] `prompts/squad/protocol/handoff-envelope.schema.md`
- [x] `prompts/squad/protocol/reasoning-log.schema.md`

### Step 4 — Slash commands and skills

- [x] `prompts/squad/commands/squad-plan.md`
- [x] `prompts/squad/commands/squad-implement.md`
- [x] `prompts/squad/commands/squad-review.md`
- [x] `prompts/squad/commands/squad-security.md`
- [x] `prompts/squad/skills/reasoning/SKILL.md`
- [x] `prompts/squad/skills/quality-gate/SKILL.md`
- [x] `prompts/squad/skills/prove-it-bugfix/SKILL.md`
- [x] `prompts/squad/README.md` (authoring contract)

### Step 5 — Sync script + manifests

- [x] `scripts/sync-squad.ps1`
- [x] `.github/plugin/plugin.json`
- [x] `.github/plugins/squad/.mcp.json`
- [x] Run `pwsh ./scripts/sync-squad.ps1` once; verify clean output.

### Step 6 — Legacy agent updates

- [x] `.github/agents/router.agent.md` → `.github/agents/model-router.agent.md` (rename via `git mv`; body unchanged).
- [x] `.github/agents/llm-gateway-architect.agent.md` → 10-line deprecation pointer to `squad.architect`.
- [x] `.github/agents/local-ai-setup.agent.md` → 10-line deprecation pointer to `squad.infra`.
- [x] `.github/agents/local.agent.md` — left untouched.

### Step 7 — Instructional pointers

- [x] `.github/copilot-instructions.md` Custom Agents section: squad entry + specialists + `model-router` + `local`.
- [x] `CLAUDE.md` — append `## Squad Guardrails` block linking `prompts/squad/_shared/*.instructions.md` and protocol references.
- [x] `.claude/settings.local.json` — add read-only `Bash(dotnet build|test|run:*)`, `Bash(git status|diff|log:*)`, `WebFetch` while preserving existing `WebSearch`.

### Step 8 — Artifact roots and planning docs

- [x] `Docs/plan/squad-orchestration-plan.md` (this file).
- [ ] `Docs/plan/llm-agent-platform-plan.md` — append Phase 3 sub-epic pointer.
- [ ] `Docs/squad/README.md` — how to drive a run.
- [ ] `Docs/squad/runs/.gitkeep` — ensure directory exists in fresh clones.

### Step 9 — Smoke tests (manual)

Run both smoke tests from ADR-0009 §Details and §Invocation ergonomics:

- [ ] Copilot CLI: `copilot plugin install ./.github/plugins/squad` → `copilot` → `/agent squad <task>`.
- [ ] Claude Code: `claude` → `/squad-plan <task>`.

Both paths must produce an identical `Docs/squad/runs/<ISO-ts>/` layout with `spec.md`, `plan.md`, `handoff/NN-*.md`, `reasoning.log.md`, and (if the diff warrants) `review/` + `security/`.

---

## 3. File inventory (final state)

### Created

| Path | Kind |
|---|---|
| `Docs/design/adr/0009-squad-orchestration.md` | ADR |
| `Docs/plan/squad-orchestration-plan.md` | Plan (this file) |
| `Docs/squad/README.md` | Run-driver guide |
| `Docs/squad/runs/.gitkeep` | Placeholder |
| `prompts/squad/README.md` | Authoring contract |
| `prompts/squad/<role>.prompt.md` × 8 | Source-of-truth agents |
| `prompts/squad/_shared/*.instructions.md` × 7 | Path-scoped guardrails |
| `prompts/squad/protocol/*.md` × 3 | Tag vocab + schemas |
| `prompts/squad/commands/*.md` × 4 | Slash-command bodies |
| `prompts/squad/skills/<name>/SKILL.md` × 3 | Shared skills |
| `scripts/sync-squad.ps1` | Idempotent generator |
| `.github/plugin/plugin.json` | Copilot CLI manifest |
| `.github/plugins/squad/.mcp.json` | MCP bundle |
| `.github/plugins/squad/{agents,skills,instructions,commands,protocol}/` | GENERATED copies |
| `.claude/{agents,skills,commands}/` | GENERATED copies |

### Edited

| Path | Change |
|---|---|
| `.github/agents/router.agent.md` | Renamed to `model-router.agent.md`. |
| `.github/agents/llm-gateway-architect.agent.md` | Body replaced with deprecation pointer. |
| `.github/agents/local-ai-setup.agent.md` | Body replaced with deprecation pointer. |
| `.github/copilot-instructions.md` | Custom Agents section rewritten. |
| `CLAUDE.md` | `## Squad Guardrails` block appended. |
| `.claude/settings.local.json` | Added read-only Bash/WebFetch permissions (preserving `WebSearch`). |
| `Docs/plan/llm-agent-platform-plan.md` | Phase 3 sub-epic pointer appended. |

### Unchanged

- All `Blaze.LlmGateway.*` .NET projects.
- ADRs 0001–0008.
- `.github/agents/local.agent.md` (Ollama on-device assistant).

---

## 4. Sync script contract

`scripts/sync-squad.ps1` is idempotent and runs on demand. Edit `prompts/squad/`, run the script, commit both sides.

### Inputs

- `prompts/squad/<role>.prompt.md` with YAML frontmatter (`name`, `description`, `model`, `tools`, `owns`).
- `prompts/squad/_shared/*.instructions.md` — copied verbatim.
- `prompts/squad/protocol/*.md` — copied verbatim to both targets.
- `prompts/squad/commands/*.md` — copied to `.claude/commands/` and `.github/plugins/squad/commands/`.
- `prompts/squad/skills/<name>/SKILL.md` — copied to `.claude/skills/<name>/` and `.github/plugins/squad/skills/<name>/`.

### Outputs

- `.github/plugins/squad/agents/squad.<role>.agent.md` — Copilot CLI variant. Tool names translated via the table below.
- `.claude/agents/squad-<role>.md` — Claude Code variant. Tool names kept in Claude casing.
- `.github/plugins/squad/{instructions,protocol,commands,skills}/` — mirror.
- `.claude/{skills,commands}/` — mirror.

### Tool-name translation

| Claude Code | Copilot CLI |
|---|---|
| `Read` | `read` |
| `Edit` | `edit` |
| `Grep` | `search` |
| `Glob` | `search` |
| `Bash` | `shell` |
| `WebFetch` | `web` |
| `Agent` | `agent` |

Model ids are bare Claude names (`claude-opus-4.7`, `claude-sonnet-4.6`, `claude-haiku-4.5`) in both targets — no `github-copilot/` prefix required, per the precedent in the legacy `llm-gateway-architect.agent.md` and Copilot CLI's Claude-model support.

### Contributor workflow

```powershell
# 1. Edit the source
code prompts/squad/coder.prompt.md

# 2. Regenerate per-target copies
pwsh ./scripts/sync-squad.ps1

# 3. Commit both source and generated output together
git add prompts/squad/ .github/plugins/squad/ .claude/
git commit -m "Squad: <summary>"
```

---

## 5. Per-run artifact layout

A single squad run creates:

```
Docs/squad/runs/<ISO-ts>-<slug>/
├── spec.md                             # Planner output
├── plan.md                             # Planner ordered steps with file assignments
├── handoff/
│   ├── 01-conductor-to-planner.md      # Envelope for phase 0
│   ├── 02-conductor-to-architect.md    # (optional) if arch gate fires
│   ├── 03-conductor-to-coder.md        # Envelope for phase 2.1
│   ├── 04-conductor-to-tester.md
│   └── NN-*.md
├── reasoning.log.md                    # Append-only decisions
├── review/
│   └── review.log.md                   # Reviewer findings (severity-ranked)
└── security/
    └── scan.md                         # Security-Review findings (if triggered)
```

Envelope schema: [`prompts/squad/protocol/handoff-envelope.schema.md`](../../prompts/squad/protocol/handoff-envelope.schema.md). Log schema: [`prompts/squad/protocol/reasoning-log.schema.md`](../../prompts/squad/protocol/reasoning-log.schema.md).

---

## 6. Verification checklist

Run before merging the squad landing PR:

- [ ] `pwsh ./scripts/sync-squad.ps1` exits 0 and `git status` shows no unstaged drift.
- [ ] All 8 source prompts have valid YAML frontmatter (`name`, `description`, `model`, `tools`, `owns`).
- [ ] All 7 `_shared/*.instructions.md` files declare `applyTo` globs.
- [ ] All 3 skill directories contain `SKILL.md`.
- [ ] `.github/plugin/plugin.json` is valid JSON and points to `./plugins/squad/{agents,skills,instructions}`.
- [ ] `.github/plugins/squad/.mcp.json` lists `microsoft-learn`, `context7`, `github`.
- [ ] `.claude/settings.local.json` preserves existing `WebSearch` entry and adds the 8 new rules.
- [ ] `CLAUDE.md` §"Squad Guardrails" links all shared instructions and protocol files.
- [ ] `.github/copilot-instructions.md` references `model-router` (not `router`) and the 8 squad agents.
- [ ] Smoke test 1 (Copilot CLI) produces `Docs/squad/runs/<ts>/` with valid handoff envelopes.
- [ ] Smoke test 2 (Claude Code) produces identical directory structure.

---

## 7. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Sync drift (contributor edits generated file by hand) | `prompts/squad/README.md` documents the rule; reviewers check. |
| MCP server outage degrades squad capability | `microsoft-learn` is the only squad dependency that must always be live. `context7` and `github` fall back to WebFetch. |
| Copilot CLI model-id prefix requirement changes | Sync script is one hashtable edit away from emitting `github-copilot/` prefixes if needed. |
| Handoff-envelope file-lock violation by Coder | Coder prompt enforces `[BLOCKED]` emission; Reviewer audits diff against envelope. |
| 40+ new markdown files increase doc surface | Source-of-truth / generated split keeps edits localized. Single-source authoring contract documented in `prompts/squad/README.md`. |

---

## 8. Rollback

If the squad surface must be reverted:

1. Remove `.github/plugin/`, `.github/plugins/squad/`, `.claude/agents/squad-*`, `.claude/skills/`, `.claude/commands/squad-*`.
2. Restore original `.github/agents/llm-gateway-architect.agent.md` and `local-ai-setup.agent.md` bodies from git history.
3. Rename `.github/agents/model-router.agent.md` back to `router.agent.md` (git mv).
4. Revert the Custom Agents section in `.github/copilot-instructions.md`.
5. Revert the `## Squad Guardrails` block in `CLAUDE.md`.
6. Revert the permission additions in `.claude/settings.local.json`.
7. Archive `Docs/squad/runs/` content to a branch; delete the `Docs/squad/` directory or leave empty `runs/.gitkeep`.
8. Mark ADR-0009 as `Superseded` with a reference to the superseding ADR.

Everything outside `Blaze.LlmGateway.*` source is markdown or JSON, so rollback is a clean revert commit.

---

## 9. Related

- [ADR-0009 — Squad orchestration](../design/adr/0009-squad-orchestration.md)
- [CLAUDE.md §Squad Guardrails](../../CLAUDE.md)
- [prompts/squad/README.md](../../prompts/squad/README.md) — authoring contract
- [Docs/squad/README.md](../squad/README.md) — how to drive a run
- [research/burkeholland-ultralight-orchestration.md](../research/burkeholland-ultralight-orchestration.md)
- [research/https-github-com-microsoft-devsquad-copilot.md](../research/https-github-com-microsoft-devsquad-copilot.md)
