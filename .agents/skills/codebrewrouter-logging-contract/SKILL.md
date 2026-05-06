---
name: codebrewrouter-logging-contract
description: Enforce the CodebrewRouter logging contract when changing router telemetry, Aspire-visible request logs, agent prompts, custom agents, or tests that cover [ROUTER-*] and [AGENT-*] tags.
---

# CodebrewRouter Logging Contract

## Overview

Use this project-level only skill whenever a change touches runtime routing logs, Aspire-visible request telemetry, custom agents, or prompt files that describe agent logging.

## Source Of Truth

Read `Docs/engineering/logging-contract.md` before editing logging behavior or agent instructions.

## Runtime Rules

- Use `RouterLog.Write(...)` for C# router telemetry.
- Router request lifecycle logs must start with an exact `[ROUTER-*]` tag from the contract.
- Keep `[ROUTER-CONTEXT]` at Debug.
- Keep `[ROUTER-SKIP]`, `[ROUTER-FAIL]`, `[ROUTER-EXHAUSTED]`, and `[ROUTER-MIDSTREAM-FAIL]` at Warning unless the call explicitly escalates to Error.
- Do not convert unrelated startup, discovery, health, or app logs into router tags.

## Agent Rules

- Keep this skill project-level only in `.agents/skills/codebrewrouter-logging-contract/`; do not install or copy it to a user-global skills directory.
- Agent lifecycle telemetry must use `[AGENT-*]`, not `[ROUTER-*]`.
- New OpenCode agents live in `.opencode/agents/`.
- New Copilot CLI agents live in `.github/agents/`.
- Codex repo skills live in `.agents/skills/`.
- New agent prompts should link to `Docs/engineering/logging-contract.md`.

## Command Rules

- Project command name: `/codebrewrouter-logging`.
- OpenCode commands live in `.opencode/commands/` and may bind `agent: codebrewrouter-logging`.
- Copilot-compatible repo commands live in `.claude/commands/`; Copilot CLI plugin commands live in `.github/plugins/codebrewrouter-logging/commands/`.
- Codex command packaging lives in `plugins/codebrewrouter-logging/` with `.codex-plugin/plugin.json` and the marketplace entry in `.agents/plugins/marketplace.json`.
- Keep all command files project-level only and link to `Docs/engineering/logging-contract.md`.

## Verification

Run:

```powershell
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter FullyQualifiedName~RouterLoggingContractTests
```

For production code changes, also run the full solution tests and `dotnet build Blaze.LlmGateway.slnx --no-incremental -warnaserror`.
