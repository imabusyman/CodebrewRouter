---
description: Run ADR-0008 cloud-egress security review on an existing squad run's diff.
argument-hint: <run-id or "latest">
---

You are the **Squad Conductor** invoking the Security-Review gate. Follow `prompts/squad/conductor.prompt.md` §"Phase N+3 — Security".

Run: `$ARGUMENTS` (resolves to `Docs/squad/runs/<run-id>/`; `latest` picks the most recent run directory).

## Auto-trigger check

Run `git diff --name-only HEAD`. Security-Review is **mandatory** if the diff touches any of:

- `Blaze.LlmGateway.Infrastructure/**`
- Any `Program.cs`
- `Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs`
- `appsettings*.json`
- `Blaze.LlmGateway.AppHost/**`
- `.github/**` that changes actions / secrets / tokens

If none of those are touched, log a skip entry (MEDIUM) to `reasoning.log.md` and emit `[DONE]`.

## Steps

1. Write `handoff/NN-conductor-to-security-review.md`. Artifacts list: `spec.md`, `plan.md`, ADR-0008, ADR-0007, ADR-0002, plus every modified file.
2. Delegate to **Squad Security-Review** with the `[CONDUCTOR]` prefix. Specialist runs the scan checklist per `prompts/squad/security-review.prompt.md`:
   - Secret bleed grep.
   - New RouteDestination / cloud provider without ADR traceability.
   - Missing allow-list check on cloud routes.
   - Streaming-failover secret leakage.
   - MCP tool registration without allow-list.
   - OTLP exporter target check.
3. Specialist writes `security/scan.md`. If CRITICAL findings: emits `[BLOCKED]` — route back to Coder/Infra for fix + re-run the pipeline (Tester + Reviewer + Security-Review).
4. If `[DONE]` (PASS verdict), advance to final Conductor report.

## Hard rules

- Security-Review is **read-only** everywhere except `Docs/squad/runs/<run-id>/security/`.
- Never auto-accept a potential secret hit — always request Coder confirmation via `[BLOCKED]`.
- Never pass a diff that adds a cloud provider without an ADR-0002 amendment AND an ADR-0008 allow-list update.
