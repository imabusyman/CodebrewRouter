# Structured-action tag vocabulary

The squad speaks to itself and to the user through a fixed tag vocabulary. Every specialist message ends with at least one tag. The Conductor both emits and consumes tags; specialists consume `[CONDUCTOR]` and emit the rest.

## Tags

| Tag | Emitter | Meaning | Conductor reaction |
|---|---|---|---|
| `[CONDUCTOR]` | Conductor | Prefixes every delegation. Signals the specialist is running in squad mode and must respond with structured-action tags. | n/a — you emit this. |
| `[ASK] <question>` | Any specialist | Needs user clarification before it can continue. | Relay to user; await reply; re-delegate with the answer attached in the next envelope. |
| `[CREATE] <path>` | Planner, Architect | Wants to create a new artifact at `<path>` (a doc, an ADR, a plan step). | Add to phase plan; confirm before the specialist writes. |
| `[EDIT] files: [<path>, ...]` | Coder, Tester, Infra | Emitted after a batch of file edits. `files:` lists every file touched in the batch. | Record file list in the handoff envelope; used by Reviewer + Security-Review. |
| `[CHECKPOINT] <note>` | Coder, Tester, Infra | Safe save-point reached — build green, tests green, or a coherent intermediate state. | Decide whether to invoke git commit. Never auto-commit without user say-so. |
| `[BLOCKED] <reason>` | Any specialist | Cannot continue. Reason must be specific (missing file, failing test, ambiguous spec, out-of-scope edit request). | Ask user, reroute to another specialist, or surface. |
| `[DONE]` | Any specialist | Envelope work is complete AND any quality gates the role owns are green. | Advance to the next phase. |

## Emission rules

- Exactly one terminator tag per turn — `[DONE]` or `[BLOCKED]`.
- `[ASK]` may appear mid-turn; if it does, that replaces `[DONE]`/`[BLOCKED]` and the specialist stops.
- `[EDIT]` and `[CHECKPOINT]` can appear multiple times in a turn. Include `[EDIT]` after each batch of writes so the Conductor can record incremental progress.
- `[CREATE]` must come before the content of the to-be-created file, so the Conductor can authorize the write.

## Forbidden

- Emitting a tag that is not in the table above.
- Ending a turn without any tag at all (the Conductor can't advance the phase).
- Claiming `[DONE]` when gates are failing — always `[BLOCKED]` with the specific failure.
- Emitting `[CONDUCTOR]` from a non-Conductor role.
