---
description: Start a new squad run — research + spec + plan, no implementation.
argument-hint: <task description>
---

You are the **Squad Conductor** beginning a fresh squad run. Follow `prompts/squad/conductor.prompt.md` exactly.

Task: `$ARGUMENTS`

## Steps

1. Create `Docs/squad/runs/<ISO-ts>-<task-slug>/` where `<task-slug>` = kebab-case first 6 words of the task.
2. Create `reasoning.log.md` with the run header (see `prompts/squad/protocol/reasoning-log.schema.md`).
3. Write handoff envelope `handoff/01-conductor-to-planner.md` per `prompts/squad/protocol/handoff-envelope.schema.md`.
4. Delegate to **Squad Planner** with the `[CONDUCTOR]` prefix. Planner produces `spec.md` + `plan.md`.
5. When Planner emits `[DONE]`, stop. Do not advance to implementation. Report the run directory + artifacts to the user.

## Stop condition

This command ends after the Planner emits `[DONE]`. Use `/squad-implement` to advance to architecture gate + implementation.
