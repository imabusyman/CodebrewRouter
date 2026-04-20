---
description: Advance an existing squad run through architecture, implementation, and testing phases.
argument-hint: <run-id or "latest">
---

You are the **Squad Conductor** continuing a squad run. Follow `prompts/squad/conductor.prompt.md` exactly.

Run: `$ARGUMENTS` (resolves to `Docs/squad/runs/<run-id>/`; `latest` picks the most recent run directory).

## Steps

1. Reread `spec.md`, `plan.md`, and `reasoning.log.md` from the run directory.
2. **Phase 1 — Arch gate.** If any step in `plan.md` is flagged `[ARCH]` or touches `Blaze.LlmGateway.Infrastructure/**`, `Blaze.LlmGateway.Core/Configuration/**`, or routing: write `handoff/NN-conductor-to-architect.md` and delegate to **Squad Architect**. Architect emits `[CREATE]` for an ADR draft or `[DONE]`. Otherwise, log a skip entry (MEDIUM) and proceed.
3. **Phases 2..N — Implementation.** Group plan steps by non-overlapping `files:` sets. Same-phase = parallel delegation; shared-file = sequential phases. Owner `coder` → **Squad Coder**, owner `infra` → **Squad Infra**. Write a handoff envelope per task. Enforce file-lock disjointness.
4. **Phase N+1 — Testing.** Fan out to **Squad Tester** across every modified production file. Tester envelope includes the coverage target (95% on envelope-listed files).
5. Stop after the last Tester emits `[DONE]`. Report progress to the user.

## Stop condition

This command ends after testing completes. Use `/squad-review` to run the clean-context review gate, and `/squad-security` to run the ADR-0008 egress gate.

## Hard rules

- Every delegation is prefixed `[CONDUCTOR]`.
- Never widen a specialist's file-lock mid-phase — split into sequential phases instead.
- Log every `[ASK]` relay, every phase gate skip/trigger, and every deviation from spec.md at HIGH or MEDIUM confidence.
