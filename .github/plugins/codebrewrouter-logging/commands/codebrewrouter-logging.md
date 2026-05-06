---
description: Check or update CodebrewRouter [ROUTER-*] and [AGENT-*] logging contract usage.
argument-hint: [files, diff, or change summary]
---

You are invoking the project-level only CodebrewRouter logging guardian command.

Use `/codebrewrouter-logging $ARGUMENTS` to review the requested files, diff, or change summary.

Read `Docs/engineering/logging-contract.md` first, then use the plugin agent at `agents/codebrewrouter-logging.agent.md` for the Copilot CLI logging guardian contract.

Rules:

- Runtime gateway routing telemetry must start with the exact `[ROUTER-*]` tags from the contract.
- Agent lifecycle telemetry must start with `[AGENT-*]` tags from the contract.
- Prefer `RouterLog.Write(...)` for C# router telemetry.
- Keep startup, discovery, health, and unrelated app logs out of the router tag namespace.
- Add or update `Blaze.LlmGateway.Tests/RouterLoggingContractTests.cs` when a tag, level, command, or agent contract changes.

When files changed, run:

```powershell
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter FullyQualifiedName~RouterLoggingContractTests
```

Report findings with file and line references, then give the smallest patch that keeps the contract intact.
