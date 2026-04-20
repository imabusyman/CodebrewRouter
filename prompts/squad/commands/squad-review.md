---
description: Run clean-context review on an existing squad run.
argument-hint: <run-id or "latest">
---

You are the **Squad Conductor** invoking the Reviewer gate. Follow `prompts/squad/conductor.prompt.md` §"Phase N+2 — Review".

Run: `$ARGUMENTS` (resolves to `Docs/squad/runs/<run-id>/`; `latest` picks the most recent run directory).

## Steps

1. Confirm implementation and testing phases have emitted `[DONE]` for the run. If not, `[BLOCKED]` with the remaining phase name.
2. Write `handoff/NN-conductor-to-reviewer.md` per `prompts/squad/protocol/handoff-envelope.schema.md`. List ONLY artifact paths — the Reviewer rereads from disk.
3. Delegate to **Squad Reviewer** with the `[CONDUCTOR]` prefix. Reviewer runs:
   - `dotnet build --no-incremental -warnaserror`
   - `dotnet test --no-build --collect:"XPlat Code Coverage"`
   - AI-code-smell scan
   - Chesterton's Fence guard on any removals
   - Severity ranking
   - Writes `review/review.log.md`
4. If Reviewer emits `[BLOCKED]`, surface the findings to the user with file:line references. Do not advance.
5. If Reviewer emits `[DONE]` (no CRITICAL/HIGH findings + build + test + coverage all green), report and advance to `/squad-security` if the diff triggers it.

## Hard rules

- Reviewer NEVER inherits chat context. The envelope carries artifact paths only.
- Reviewer may not mark `[DONE]` with CRITICAL or HIGH findings open.
- Coverage gate is 95% line coverage on files listed in the envelope.
