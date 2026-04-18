# AGENTS.md

## Project Overview

The Awesome GitHub Copilot repository is a community-driven collection of custom agents and instructions designed to enhance GitHub Copilot experiences across various domains, languages, and use cases. The project includes:

- **Agents** - Specialized GitHub Copilot agents that integrate with MCP servers
- **Instructions** - Coding standards and best practices applied to specific file patterns
- **Skills** - Self-contained folders with instructions and bundled resources for specialized tasks
- **Hooks** - Automated workflows triggered by specific events during development
- **Workflows** - [Agentic Workflows](https://github.github.com/gh-aw) for AI-powered repository automation in GitHub Actions
- **Plugins** - Installable packages that group related agents, commands, and skills around specific themes

## Repository Structure

```text
.
├── agents/           # Custom GitHub Copilot agent definitions (.agent.md files)
├── instructions/     # Coding standards and guidelines (.instructions.md files)
├── skills/           # Agent Skills folders (each with SKILL.md and optional bundled assets)
├── hooks/            # Automated workflow hooks (folders with README.md + hooks.json)
├── workflows/        # Agentic Workflows (.md files for GitHub Actions automation)
├── plugins/          # Installable plugin packages (folders with plugin.json)
├── docs/             # Documentation for different resource types
├── eng/              # Build and automation scripts
└── scripts/          # Utility scripts
```

## Setup Commands

```bash
# Install dependencies
npm ci

# Build the project (generates README.md and marketplace.json)
npm run build

# Validate plugin manifests
npm run plugin:validate

# Generate marketplace.json only
npm run plugin:generate-marketplace

# Create a new plugin
npm run plugin:create -- --name <plugin-name>

# Validate agent skills
npm run skill:validate

# Create a new skill
npm run skill:create -- --name <skill-name>
```

## Development Workflow

### Working with Agents, Instructions, Skills, and Hooks

All agent files (`*.agent.md`) and instruction files (`*.instructions.md`) must include proper markdown front matter. Agent Skills are folders containing a `SKILL.md` file with frontmatter and optional bundled assets. Hooks are folders containing a `README.md` with frontmatter and a `hooks.json` configuration file.
