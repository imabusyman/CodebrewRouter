# `prompts/squad/` — Blaze.LlmGateway development squad (source of truth)

This directory is the **single source of truth** for the 8-agent Claude-powered development squad that serves Blaze.LlmGateway. Agent prompts, shared guardrails, protocol schemas, and slash commands all live here and are materialized to `.github/plugins/squad/` (GitHub Copilot CLI) and `.claude/` (Claude Code) by `scripts/sync-squad.ps1`.

See [ADR-0009 — Squad orchestration](../../Docs/design/adr/0009-squad-orchestration.md) for the governing architectural decision, and [`Docs/plan/squad-orchestration-plan.md`](../../Docs/plan/squad-orchestration-plan.md) for the tactical rollout plan.

## Layout

```
prompts/squad/
├── README.md                               ← this file
├── conductor.prompt.md                     ← entry-point orchestrator (never codes)
├── planner.prompt.md                       ← spec + ordered steps + file assignments
├── architect.prompt.md                     ← ADR author; MEAI pipeline authority
├── coder.prompt.md                         ← MEAI-compliant C# implementation
├── tester.prompt.md                        ← xUnit + Moq + SSE integration
├── reviewer.prompt.md                      ← clean-context diff review
├── infra.prompt.md                         ← AppHost / Aspire / secrets
├── security-review.prompt.md               ← ADR-0008 cloud-egress guard
│
├── _shared/                                ← path-scoped guardrails (applyTo globs)
│   ├── guardrails.instructions.md          ← universal rules every specialist obeys
│   ├── meai-infrastructure.instructions.md ← Infrastructure/Api/Core rules
│   ├── aspire-apphost.instructions.md      ← AppHost/ServiceDefaults rules
│   ├── tests.instructions.md               ← Tests/Benchmarks rules
│   ├── adr.instructions.md                 ← Docs/design/adr rules
│   ├── cloud-egress.instructions.md        ← ADR-0008 default-deny
│   └── style.instructions.md               ← C# style + nullability + build gate
│
├── protocol/
│   ├── structured-actions.md               ← tag vocabulary ([ASK], [EDIT], [DONE], ...)
│   ├── handoff-envelope.schema.md          ← envelope format for every delegation
│   └── reasoning-log.schema.md             ← append-only reasoning log format
│
└── commands/
    ├── squad-plan.md                       ← /squad-plan entry point
    ├── squad-implement.md                  ← /squad-implement
    ├── squad-review.md                     ← /squad-review
    └── squad-security.md                   ← /squad-security
```

## Authoring rules

1. **Edit source first.** All changes start here in `prompts/squad/`. Never edit under `.github/plugins/squad/` or `.claude/` by hand — those are generated.
2. **Keep frontmatter minimal.** Each role prompt's YAML frontmatter is:
   ```yaml
   ---
   name: <role name>
   description: <one-paragraph summary for agent selection>
   model: claude-opus-4.7 | claude-sonnet-4.6 | claude-haiku-4.5
   tools: [Read, Edit, Grep, Glob, Bash, WebFetch, Agent]
   owns: [<repo-relative glob>, ...]
   ---
   ```
   - `model` — bare Claude model id. Copilot CLI accepts these without the `github-copilot/` prefix.
   - `tools` — use Claude-Code vocabulary in the source. The sync script translates to Copilot-CLI vocabulary (`read`, `edit`, `search`, `shell`, `github`, `web`, `agent`) for `.github/plugins/squad/`.
   - `owns` — the set of paths this role may write to. Enforced by the Conductor via handoff envelopes.
3. **No emoji in generated content.** Squad agents produce code, ADRs, and logs consumed by tools and humans — keep it machine-friendly.
4. **Protocol is source-only.** `protocol/` files describe tag vocabulary, envelope format, and log format. They are copied verbatim to both targets by the sync script.
5. **Commands live in `commands/`.** Each `commands/<name>.md` file has its own YAML frontmatter (`description`, `argument-hint`) and a body that directs the Conductor. The sync script emits these as:
   - `.claude/commands/<name>.md` for Claude Code slash commands.
   - Documentation references under `.github/plugins/squad/` for Copilot CLI (Copilot exposes the squad as `/agent squad`; command shims are informational there).

## Contributor workflow

```powershell
# 1. Edit a role prompt, guardrail, or command
code prompts/squad/coder.prompt.md

# 2. Regenerate per-target copies
pwsh ./scripts/sync-squad.ps1

# 3. Inspect the diff the sync produced
git diff .github/plugins/squad .claude

# 4. Commit source and generated output together
git add prompts/squad .github/plugins/squad .claude
git commit -m "Squad: <scope>: <change>"
```

`scripts/sync-squad.ps1` is idempotent and runs on demand. It is NOT CI-enforced; drift is caught at PR review time. If you see drift in `.github/plugins/squad/` or `.claude/` that does not match `prompts/squad/`, run the sync script locally and commit the regeneration as a separate PR.

## Running the squad

- **Claude Code.** `/squad-plan <task>` starts a new run. `/squad-implement latest` advances through arch gate + implementation + testing. `/squad-review latest` runs the clean-context review gate. `/squad-security latest` runs the ADR-0008 cloud-egress gate.
- **GitHub Copilot CLI.** `copilot plugin install ./.github/plugins/squad` then `/agent squad` and describe the task. The Conductor phases through the squad automatically.

Every run leaves artifacts under `Docs/squad/runs/<ISO-ts>-<task-slug>/` — see `Docs/squad/README.md` for the artifact layout.
