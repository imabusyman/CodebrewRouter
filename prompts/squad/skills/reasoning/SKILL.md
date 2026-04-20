---
name: reasoning
description: Maintain the append-only reasoning log and write handoff envelopes for every squad delegation. Use this when the Conductor delegates to a specialist or when a specialist records a non-trivial decision.
---

# Reasoning — squad decision log + handoff envelopes

This skill formalizes two disk artifacts that make squad runs auditable:

1. **`reasoning.log.md`** — append-only log of non-trivial decisions.
2. **`handoff/NN-from-to-to.md`** — per-delegation envelope.

Follow the schemas exactly. Trivial edits (typo, method rename) do not belong in the log.

## Reasoning log

Write one `##`-level entry per decision. Schema (from `prompts/squad/protocol/reasoning-log.schema.md`):

```markdown
## <ISO-8601 UTC> — <role> — <HIGH | MEDIUM | LOW>
Decision: <one factual sentence>
Rationale: <one or two sentences — why this, not something else>
Evidence: <artifact path, ADR number, PRD section, code line>
```

### Log these (always)

- Every phase gate skipped or triggered (MEDIUM).
- Every `[ASK]` relay and user answer (MEDIUM or HIGH).
- Every architectural decision committed to an ADR draft (HIGH).
- Every deviation from `CLAUDE.md` / ADR defaults (HIGH).
- Every mid-run file-lock change (HIGH).

## Handoff envelope

One file per delegation at `Docs/squad/runs/<run-id>/handoff/<NN>-<from>-to-<to>.md`.

Schema from `prompts/squad/protocol/handoff-envelope.schema.md`:

```yaml
---
from: <role>
to: <role>
phase: <integer>
task: <phase>.<task>
timestamp: <ISO-8601>
run_id: <run-id>
---

## Artifacts to re-read (required)
- <path>

## Files you may edit (exclusive lock, this phase)
- <path> (create | edit)

## Files other parallel tasks own (DO NOT TOUCH)
- <path>

## Inherited assumptions
- <decision>

## Pending decisions
- (none)

## Discarded context
- <option rejected earlier>
```

Validation:
- File-lock sets must be disjoint across parallel tasks in the same phase.
- Every "Artifacts to re-read" path must exist on disk.
- Paths are repo-relative with forward slashes.
