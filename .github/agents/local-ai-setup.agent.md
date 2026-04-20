---
name: Local AI Setup (deprecated)
description: Deprecated — use the Squad Infra specialist instead. See .github/plugins/squad/agents/squad.infra.agent.md or run /agent squad for full multi-agent orchestration.
model: claude-haiku-4.5
---

# Deprecated

This agent has been absorbed into the **Blaze.LlmGateway development squad**.

- New Copilot CLI entry: `/agent squad` (after `copilot plugin install ./.github/plugins/squad`), or `/agent squad.infra` for the infrastructure role directly.
- New Claude Code entry: the `squad-infra` agent at `.claude/agents/squad-infra.md`.
- Governing ADR: [../../Docs/design/adr/0009-squad-orchestration.md](../../Docs/design/adr/0009-squad-orchestration.md).
- Source of truth: [`prompts/squad/infra.prompt.md`](../../prompts/squad/infra.prompt.md).

The squad Infra role covers Azure Foundry Local, GitHub Models, Ollama containers, Aspire secret wiring, and runs on `claude-haiku-4.5` for quick scaffolding and diagnostic turnaround.
