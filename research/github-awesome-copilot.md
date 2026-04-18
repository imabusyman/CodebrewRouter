# GitHub Awesome Copilot — Reference Note

**Upstream repository:** [github/awesome-copilot](https://github.com/github/awesome-copilot)  
**Website:** [awesome-copilot.github.com](https://awesome-copilot.github.com)  
**Use in this repo:** install as a Copilot plugin; keep this file only as a reference note.

## Executive Summary

`github/awesome-copilot` is a curated catalog of GitHub Copilot customizations, including agents, instructions, skills, hooks, workflows, and installable plugins. For this repository, the simplest approach is to **install the upstream plugin directly** and keep only a lightweight markdown reference here, rather than mirroring the upstream source tree into `research\sources\`.

This keeps the local research archive focused on **what to use** and **where to find it**, while avoiding duplication of a large upstream catalog that will be consumed through the plugin system anyway.

## Recommended usage

If the Awesome Copilot marketplace is not already registered:

```bash
copilot plugin marketplace add github/awesome-copilot
```

Install a plugin from the marketplace:

```bash
copilot plugin install <plugin-name>@awesome-copilot
```

## What it provides

The upstream repository organizes Copilot customizations into these categories:

| Category | Purpose | Browse |
|---|---|---|
| Agents | Specialized Copilot personas and domain experts | [Agents](https://awesome-copilot.github.com/agents) |
| Instructions | Reusable coding guidance and prompt scaffolding | [Instructions](https://awesome-copilot.github.com/instructions) |
| Skills | Task-focused reusable capabilities | [Skills](https://awesome-copilot.github.com/skills) |
| Plugins | Installable bundles of agents and skills | [Plugins](https://awesome-copilot.github.com/plugins) |
| Hooks | Guardrails and automation during agent sessions | [Hooks](https://awesome-copilot.github.com/hooks) |
| Workflows | Agentic GitHub Actions automation | [Workflows](https://awesome-copilot.github.com/workflows) |

## Relevant upstream references

- Main repository: [github/awesome-copilot](https://github.com/github/awesome-copilot)
- Website index: [awesome-copilot.github.com](https://awesome-copilot.github.com)
- Tools page: [awesome-copilot.github.com/tools](https://awesome-copilot.github.com/tools)
- Learning Hub: [awesome-copilot.github.com/learning-hub](https://awesome-copilot.github.com/learning-hub)

Representative upstream assets that are especially relevant to CodebrewRouter:

- .NET-oriented plugin bundle: [plugins/csharp-dotnet-development/.github/plugin/plugin.json](https://github.com/github/awesome-copilot/blob/main/plugins/csharp-dotnet-development/.github/plugin/plugin.json)
- Agent safety guidance: [instructions/agent-safety.instructions.md](https://github.com/github/awesome-copilot/blob/main/instructions/agent-safety.instructions.md)
- C# expert agent: [agents/CSharpExpert.agent.md](https://github.com/github/awesome-copilot/blob/main/agents/CSharpExpert.agent.md)
- Tool guardrail hook: [hooks/tool-guardian/README.md](https://github.com/github/awesome-copilot/blob/main/hooks/tool-guardian/README.md)
- Example workflow: [workflows/daily-issues-report.md](https://github.com/github/awesome-copilot/blob/main/workflows/daily-issues-report.md)

## Why this repo keeps only a reference

Because the plugin will be installed directly from the upstream marketplace, copying all upstream skills, prompts, hooks, and plugin metadata into this repo adds maintenance cost without improving runtime behavior. A reference-only note is enough to:

1. remind contributors what `awesome-copilot` is,
2. link to the upstream catalog,
3. point to a few especially relevant assets,
4. document the install path used by this repository.

## Confidence Assessment

**High confidence**

- `awesome-copilot` is suitable as a linked reference when the actual consumption model is plugin installation from upstream.
- A reference-only markdown is simpler and better aligned with your stated workflow than maintaining a local mirrored source snapshot for this specific upstream.

**Tradeoff**

- This entry is intentionally lighter than the other mirrored research reports in this repo. That is acceptable here because the goal is reference and discoverability, not archival source preservation.
