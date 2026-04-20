# Handoff-envelope schema

The Conductor writes one handoff envelope per delegation to `Docs/squad/runs/<ISO-ts>/handoff/<NN>-<from>-to-<to>.md`. The specialist reads it as its sole source of truth for the task.

## Path format

```
Docs/squad/runs/<ISO-ts>-<task-slug>/handoff/<NN>-<from>-to-<to>.md
```

- `<ISO-ts>` — run start timestamp, e.g. `2026-04-19T14-30-00Z` (colons replaced with hyphens for filesystem safety).
- `<task-slug>` — kebab-case first 6 words of the user request.
- `<NN>` — two-digit sequence within the run, starting `01`.
- `<from>` — role emitting the delegation (always `conductor` at top level; nested envelopes use the parent role).
- `<to>` — receiving role, e.g. `planner`, `coder`, `tester`.

## File contents

YAML frontmatter followed by markdown body. **No heading levels above `##`** inside the body — the file itself is the envelope.

```yaml
---
from: conductor
to: coder
phase: 2
task: 2.1
timestamp: 2026-04-19T14:32:00Z
run_id: 2026-04-19T14-30-00Z-add-circuit-breaker
---

## Artifacts to re-read (required)
- <absolute or repo-relative path>
- ...

## Files you may edit (exclusive lock, this phase)
- <path> (create | edit)
- ...

## Files other parallel tasks own (DO NOT TOUCH)
- <path>        <!-- owned by: <role>, phase <N> -->
- ...

## Inherited assumptions
- <one-line decision from spec.md or prior ADR that this task must honor>
- ...

## Pending decisions
- (none)                <!-- if empty, specialist proceeds -->
- <question>            <!-- if populated, specialist emits [ASK] -->

## Discarded context
- <option rejected earlier in the run — do not reintroduce>
- ...
```

## Frontmatter fields

| Field | Required | Description |
|---|---|---|
| `from` | yes | Emitting role (`conductor`, `planner`, `architect`, ...). |
| `to` | yes | Receiving role. |
| `phase` | yes | Integer phase number (0 = research, 1 = arch gate, 2+ = implementation). |
| `task` | yes | Sub-task id (`<phase>.<task>`), matches the Planner's `plan.md` step. |
| `timestamp` | yes | ISO-8601 time the envelope was written. |
| `run_id` | yes | `<ISO-ts>-<task-slug>` — groups envelopes under one run. |

## Body sections

### Artifacts to re-read (required)

Every path the specialist must read from disk **before forming any opinion**. Always includes `spec.md` and `plan.md`; includes every ADR referenced by spec.md; includes every source file the task reads or edits.

### Files you may edit (exclusive lock, this phase)

The specialist may write only these paths. Touching any path outside the list is `[BLOCKED]` with the path requested.

### Files other parallel tasks own (DO NOT TOUCH)

Explicit denylist of files other tasks in the same phase own. The specialist must emit `[BLOCKED]` rather than silently edit.

### Inherited assumptions

Decisions already made (by the Planner in `spec.md`, by an Architect ADR, or by a prior phase) that this task is bound by. Specialists do not revisit these.

### Pending decisions

If empty: proceed. If populated: the specialist emits `[ASK]` with the question verbatim and stops.

### Discarded context

Options earlier phases explicitly rejected. The specialist must not reintroduce any of them. If the specialist believes a discarded option should be revived, it must emit `[ASK]` — never silently switch approaches.

## Validation rules

- **File-lock disjointness** — when the Conductor delegates N tasks in parallel within one phase, the union of all exclusive-lock sets must have no duplicates. Overlapping file sets → split into sequential phases.
- **Artifacts present on disk** — every path under "Artifacts to re-read" must exist before the envelope is written.
- **Path style** — repo-relative paths throughout, forward slashes.
