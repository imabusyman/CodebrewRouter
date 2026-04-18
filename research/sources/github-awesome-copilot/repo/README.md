# 🤖 Awesome GitHub Copilot
[![Powered by Awesome Copilot](https://img.shields.io/badge/Powered_by-Awesome_Copilot-blue?logo=githubcopilot)](https://aka.ms/awesome-github-copilot) [![GitHub contributors from allcontributors.org](https://img.shields.io/github/all-contributors/github/awesome-copilot?color=ee8449)](#contributors-)

A community-created collection of custom agents, instructions, skills, hooks, workflows, and plugins to supercharge your GitHub Copilot experience.

> [!TIP]
> **Explore the full collection on the website →** [awesome-copilot.github.com](https://awesome-copilot.github.com)
>
> The website offers full-text search and filtering across hundreds of resources, plus the [Tools](https://awesome-copilot.github.com/tools) section for MCP servers and developer tooling, and the [Learning Hub](https://awesome-copilot.github.com/learning-hub) for guides and tutorials.
>
> **Using this collection in an AI agent?** A machine-readable [`llms.txt`](https://awesome-copilot.github.com/llms.txt) is available with structured listings of all agents, instructions, and skills.

## 📖 Learning Hub

New to GitHub Copilot customization? The **[Learning Hub](https://awesome-copilot.github.com/learning-hub)** on the website offers curated articles, walkthroughs, and reference material — covering everything from core concepts like agents, skills, and instructions to hands-on guides for hooks, agentic workflows, MCP servers, and the Copilot coding agent.

## What's in this repo

| Resource | Description | Browse |
|----------|-------------|--------|
| 🤖 [Agents](docs/README.agents.md) | Specialized Copilot agents that integrate with MCP servers | [All agents →](https://awesome-copilot.github.com/agents) |
| 📋 [Instructions](docs/README.instructions.md) | Coding standards applied automatically by file pattern | [All instructions →](https://awesome-copilot.github.com/instructions) |
| 🎯 [Skills](docs/README.skills.md) | Self-contained folders with instructions and bundled assets | [All skills →](https://awesome-copilot.github.com/skills) |
| 🔌 [Plugins](docs/README.plugins.md) | Curated bundles of agents and skills for specific workflows | [All plugins →](https://awesome-copilot.github.com/plugins) |
| 🪝 [Hooks](docs/README.hooks.md) | Automated actions triggered during Copilot agent sessions | [All hooks →](https://awesome-copilot.github.com/hooks) |
| ⚡ [Agentic Workflows](docs/README.workflows.md) | AI-powered GitHub Actions automations written in markdown | [All workflows →](https://awesome-copilot.github.com/workflows) |
| 🍳 [Cookbook](cookbook/README.md) | Copy-paste-ready recipes for working with Copilot APIs | — |

## 🛠️ Tools

Looking at how to use Awesome Copilot? Check out the **[Tools section](https://awesome-copilot.github.com/tools)** of the website for MCP servers, editor integrations, and other developer tooling to get the most out of this collection.

## Install a Plugin

For most users, the **Awesome Copilot** marketplace is already registered in the Copilot CLI/VS Code, so you can install a plugin directly:

```bash
copilot plugin install <plugin-name>@awesome-copilot
```

If you are using an older Copilot CLI version or a custom setup and see an error that the marketplace is unknown, register it once and then install:

```bash
copilot plugin marketplace add github/awesome-copilot
copilot plugin install <plugin-name>@awesome-copilot
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) · [AGENTS.md](AGENTS.md) for AI agent guidance · [Security](SECURITY.md) · [Code of Conduct](CODE_OF_CONDUCT.md)

> The customizations here are sourced from third-party developers. Please inspect any agent and its documentation before installing.
