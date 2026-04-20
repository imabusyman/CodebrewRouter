# Reasoning-log schema

Each squad run maintains an append-only reasoning log at `Docs/squad/runs/<ISO-ts>-<slug>/reasoning.log.md`. The Conductor owns the file; specialists emit log-worthy decisions via their response body and the Conductor records them.

## Path

```
Docs/squad/runs/<ISO-ts>-<task-slug>/reasoning.log.md
```

## File header

Written once when the Conductor creates the run directory:

```markdown
# Reasoning log — <task slug>

- Run id: <ISO-ts>-<task-slug>
- Started: <ISO-8601>
- User request: <verbatim>
- Conductor model: claude-opus-4.7
```

## Entry format

Each entry is an `##`-level section with timestamp + emitter + confidence tag, followed by Decision / Rationale / Evidence lines. Entries are append-only — never edit prior entries.

```markdown
## <ISO-8601 timestamp> — <role> — <HIGH | MEDIUM | LOW>
Decision: <one-sentence factual statement of what was chosen>
Rationale: <one or two sentences — why this, not something else>
Evidence: <artifact paths, ADR numbers, PRD section, code line>
```

### Confidence tag meaning

| Tag | When to use |
|---|---|
| HIGH | Load-bearing decision — other tasks depend on it. Always logged. E.g. "circuit breaker threshold = 5". |
| MEDIUM | Non-trivial choice between viable alternatives. E.g. "inline breaker vs extract library". |
| LOW | Minor choice with low downside. Optional; log only if a reviewer might reasonably ask. |

## What counts as log-worthy

Always log:
- Every phase gate **skipped** or **triggered** (MEDIUM).
- Every `[ASK]` relay to the user and the user's answer (MEDIUM or HIGH).
- Every architectural decision committed to an ADR draft (HIGH).
- Every deviation from a default in `CLAUDE.md` / ADRs (HIGH).
- Every time a file-lock is changed mid-run (HIGH).

Do not log:
- Trivial edits (method renames, typos).
- Build / test failures that were immediately fixed — those belong in `[CHECKPOINT]` notes.

## Example run

```markdown
# Reasoning log — add-circuit-breaker

- Run id: 2026-04-19T14-30-00Z-add-circuit-breaker
- Started: 2026-04-19T14:30:02Z
- User request: add a circuit breaker to LlmRoutingChatClient per CLAUDE.md Known Incomplete Areas
- Conductor model: claude-opus-4.7

## 2026-04-19T14:32:10Z — planner — HIGH
Decision: threshold=5 (not 3) for circuit-breaker open
Rationale: matches existing retry budget (3 retries + 2 health probes = 5)
Evidence: prompts/squad/architect.prompt.md §"Circuit breaker shape"

## 2026-04-19T14:33:45Z — architect — HIGH
Decision: skip ADR — change implements an existing ADR-0001 intent, does not alter provider identity
Rationale: ADR-0001 already calls for resilient routing; this is implementation, not a new decision
Evidence: Docs/design/adr/0001-primary-host-boundary.md §Consequences

## 2026-04-19T14:35:02Z — coder — MEDIUM
Decision: inline breaker state in LlmRoutingChatClient rather than extract library
Rationale: ADR-0009 §Consequences — "prompts-only footprint; no new NuGets this phase"
Evidence: Docs/design/adr/0009-squad-orchestration.md

## 2026-04-19T14:48:31Z — conductor — MEDIUM
Decision: trigger Security-Review gate
Rationale: diff touches Blaze.LlmGateway.Infrastructure/** per auto-trigger list
Evidence: git diff --name-only HEAD
```

## Hard rules

- Append-only. Editing prior entries destroys audit trail.
- One decision per entry. Don't batch "we decided X and also Y" — two entries.
- Timestamps are ISO-8601 UTC (`Z` suffix), not local time.
- Evidence citations are machine-checkable (file path + section, or URL).
