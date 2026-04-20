---
name: LLM Gateway Architect (deprecated)
description: Deprecated — use the Squad Architect instead. See .github/plugins/squad/agents/squad.architect.agent.md or run /agent squad for full multi-agent orchestration.
model: claude-opus-4.7
---

# Deprecated

This agent has been absorbed into the **Blaze.LlmGateway development squad**.

- New Copilot CLI entry: `/agent squad` (after `copilot plugin install ./.github/plugins/squad`), or `/agent squad.architect` for the architect role directly.
- New Claude Code entry: the `squad-architect` agent at `.claude/agents/squad-architect.md`.
- Governing ADR: [../../Docs/design/adr/0009-squad-orchestration.md](../../Docs/design/adr/0009-squad-orchestration.md).
- Source of truth: [`prompts/squad/architect.prompt.md`](../../prompts/squad/architect.prompt.md).

The squad Architect uses the current MEAI API (`GetResponseAsync` / `GetStreamingResponseAsync`), covers all 9 providers, and runs on `claude-opus-4.7`.
