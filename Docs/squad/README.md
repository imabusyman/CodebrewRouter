# Docs/squad — Per-run artifacts for the Blaze.LlmGateway development squad

This directory is the **runtime disk surface** for the 8-agent Claude-powered development squad (ADR-0009). Every squad invocation — whether from Copilot CLI or Claude Code — lands its artifacts under `runs/<ISO-ts>-<slug>/` here. Reviewer and Security-Review reread these artifacts with clean context, so the directory layout is a load-bearing part of the protocol, not just a log.

---

## Quick start

### Copilot CLI

```powershell
# One-time plugin install
copilot plugin install ./.github/plugins/squad

# Drive a run
copilot
```

Inside the CLI:

```
/agent squad
add a circuit breaker to LlmRoutingChatClient per CLAUDE.md Known Incomplete Areas
```

### Claude Code

```powershell
claude
```

Inside Claude Code:

```
/squad-plan add a circuit breaker to LlmRoutingChatClient per CLAUDE.md Known Incomplete Areas
```

Or advance phases individually: `/squad-implement`, `/squad-review`, `/squad-security`.

Both surfaces produce the same on-disk layout described below.

---

## Anatomy of a run

Every run creates one directory:

```
Docs/squad/runs/<ISO-ts>-<short-slug>/
├── spec.md                             # Planner — task brief + acceptance criteria
├── plan.md                             # Planner — ordered steps with file assignments
├── reasoning.log.md                    # Append-only HIGH/MEDIUM/LOW decisions
├── handoff/
│   ├── 01-conductor-to-planner.md      # Phase 0
│   ├── 02-conductor-to-architect.md    # Phase 1 (optional)
│   ├── 03-conductor-to-coder.md        # Phase 2.1
│   ├── 04-conductor-to-tester.md       # Phase N+1
│   └── NN-*.md
├── review/
│   └── review.log.md                   # Reviewer — severity-ranked findings
└── security/
    └── scan.md                         # Security-Review — ADR-0008 egress audit
```

Naming conventions:

- `<ISO-ts>` is `yyyy-MM-ddTHH-mm-ssZ` (filesystem-safe ISO-8601 in UTC, colons replaced with dashes).
- `<short-slug>` is a 2–5-word kebab-case summary of the user's task (e.g. `add-circuit-breaker`).
- Handoff filenames are `NN-<from-role>-to-<to-role>.md` with a zero-padded two-digit counter (`01`, `02`, ...).

### File formats

- **`spec.md`** — markdown with the user's task, confirmed assumptions, constraints, acceptance criteria.
- **`plan.md`** — markdown with ordered steps. Each step carries an explicit `files:` list for file-lock disjointness.
- **`handoff/NN-*.md`** — YAML frontmatter + markdown body. Schema: [`prompts/squad/protocol/handoff-envelope.schema.md`](../../prompts/squad/protocol/handoff-envelope.schema.md).
- **`reasoning.log.md`** — append-only decision log. Schema: [`prompts/squad/protocol/reasoning-log.schema.md`](../../prompts/squad/protocol/reasoning-log.schema.md).
- **`review/review.log.md`** — Reviewer's severity-ranked findings (BLOCKER / MAJOR / MINOR / NIT) + build/coverage gate results.
- **`security/scan.md`** — Security-Review's ADR-0008 audit: secret-bleed check, auth/allow-list check, cloud-provider additions, new `RouteDestination` additions.

---

## Phase lifecycle

The Conductor advances the run through these phases, each producing specific artifacts:

| Phase | Agent | Produces | Trigger |
|---|---|---|---|
| 0 — Research | Planner | `spec.md`, `plan.md`, `handoff/01-*.md` | Always |
| 1 — Arch gate | Architect | `Docs/design/adr/NNNN-*.md`, `handoff/02-*.md` | Pipeline / routing / config touched |
| 2..N — Impl | Coder, Infra | Source edits, `handoff/03-*.md` ... | Always (parallel where file-disjoint) |
| N+1 — Tests | Tester | `Blaze.LlmGateway.Tests/**` edits, `handoff/NN-*.md` | Always |
| N+2 — Review | Reviewer | `review/review.log.md` | Always; reruns `dotnet build -warnaserror` + coverage |
| N+3 — Security | Security-Review | `security/scan.md` | Infrastructure/**, Program.cs, LlmGatewayOptions.cs, appsettings*.json, AppHost/** touched |
| N+4 — Report | Conductor | Final summary with artifact links | Always |

Only the Conductor talks to the user. Specialists communicate via structured tags (`[ASK]`, `[CREATE]`, `[EDIT]`, `[CHECKPOINT]`, `[BLOCKED]`, `[DONE]`). See [`prompts/squad/protocol/structured-actions.md`](../../prompts/squad/protocol/structured-actions.md).

---

## Clean-context review invariant

**The artifacts under `Docs/squad/runs/<ts>/` are authoritative.** Reviewer and Security-Review are launched with a fresh chat context and told to re-read these files from disk. This is the strongest guarantee in the protocol — it catches cases where Conductor's in-memory summary drifted from what was actually decided, and it makes every run independently auditable after the fact.

Never edit artifacts from a prior run. If you find an error after the run finished, record the correction in a **new** run under a fresh `<ISO-ts>-<slug>/` directory with a `spec.md` that references the earlier run.

---

## Retention and hygiene

- Runs are **checked into git** for traceability. Treat them as part of the project history.
- Squash to a single commit per merged PR (one run per feature is common; multiple runs per feature are fine — the review logs tell the story).
- No auto-cleanup. Consider archiving runs older than 6 months to a branch or external store if the directory grows large.
- `runs/.gitkeep` guarantees the directory exists in fresh clones.

---

## Troubleshooting a run

### "The Conductor delegated but nothing happened"

- Check `handoff/NN-*.md` exists — if not, the Conductor didn't emit `[CONDUCTOR]`.
- Check the envelope's `to:` field matches a squad agent name (`planner`, `coder`, `tester`, `reviewer`, `architect`, `infra`, `security-review`).
- Verify the plugin is installed: `copilot plugin list` or that `.claude/agents/squad-*.md` are present.

### "The Reviewer / Security-Review didn't run"

- Reviewer is always required; if missing, Conductor failed to advance phases. Check `reasoning.log.md` for the last HIGH-confidence entry.
- Security-Review is auto-triggered only on sensitive diffs (see Phase N+3 trigger list above). If your change touches a listed path and Security-Review didn't run, the Conductor made an error — re-invoke with `/squad-security`.

### "The Coder edited a file outside the envelope lock"

Policy violation. The Coder prompt enforces `[BLOCKED]` when asked to touch a file outside the envelope. If this happened:

1. Revert the offending edit.
2. Record the incident in `reasoning.log.md` with severity HIGH.
3. Re-invoke the Conductor with a corrected envelope listing the actual files needed.

### "`dotnet build -warnaserror` failed in Reviewer phase"

Review log's `BLOCKER` findings are the starting point. Conductor should loop back to the Coder with an envelope scoped to the failing file. Do not mark the run complete until the build is green.

---

## Related

- [ADR-0009 — Squad orchestration](../design/adr/0009-squad-orchestration.md)
- [squad-orchestration-plan.md](../plan/squad-orchestration-plan.md) — tactical implementation plan
- [prompts/squad/README.md](../../prompts/squad/README.md) — authoring contract (edit there, run `sync-squad.ps1`)
- [prompts/squad/protocol/](../../prompts/squad/protocol/) — tag vocabulary + schemas
- [CLAUDE.md §Squad Guardrails](../../CLAUDE.md)
