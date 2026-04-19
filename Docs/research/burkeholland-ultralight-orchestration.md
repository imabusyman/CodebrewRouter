# Research Report: burkeholland/ainstall.md — Ultralight Orchestration

**Date:** 2026-04-18  
**Source:** GitHub Gist by Burke Holland

---

## Executive Summary

`ainstall.md` is not a standalone GitHub repository — it is a **GitHub Gist** (ID: `0e68481f96e94bbb98134fa6efd00436`) by Burke Holland ([@burkeholland](https://github.com/burkeholland)) that serves as the installation guide for the **Ultralight Orchestration** system. The system is a minimal, multi-agent AI coding framework for VS Code and VS Code Insiders that coordinates four specialized GitHub Copilot custom agents — Orchestrator, Planner, Coder, and Designer — powered by Claude Opus 4.6, GPT-5.3-Codex, and Gemini 3.1 Pro respectively. It provides structured, parallel task execution with file-level conflict prevention. A companion demo repository ([burkeholland/ultralight](https://github.com/burkeholland/ultralight)) houses the same agent definitions alongside a full HTML/CSS/JS demo application.

---

## Context and Background

Burke Holland is a developer advocate at Microsoft focused on GitHub Copilot and VS Code tooling. The Ultralight Orchestration system represents a practical, production-oriented approach to multi-agent AI coding inside VS Code's Copilot Chat extension.

The Gist itself (`ainstall.md`) is the canonical install document that:

1. Lists all four agents with one-click VS Code install badges.
2. Provides VS Code settings required to enable subagent functionality.
3. Summarizes each agent's role, model assignment, and toolset.
4. Contains the full system prompts for all four agents inline.

The companion repo [burkeholland/ultralight](https://github.com/burkeholland/ultralight) mirrors the agent files under `agents/` and is the live demo app used in presentations and tutorials.[^1]

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────┐
│                  User Prompt (VS Code Chat)            │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────┐
│         ORCHESTRATOR (Claude Opus 4.6)                  │
│  • Breaks down request into phases                      │
│  • Detects file-level parallelism                       │
│  • Calls subagents (never codes itself)                 │
└────────┬──────────────────┬──────────────────┬──────────┘
         │                  │                  │
         ▼                  ▼                  ▼
┌──────────────┐   ┌──────────────┐   ┌──────────────────┐
│   PLANNER    │   │    CODER     │   │    DESIGNER      │
│ (Opus 4.6)  │   │ (GPT-5.3-   │   │ (Gemini 3.1 Pro) │
│             │   │  Codex)      │   │                  │
│ Researches  │   │ Implements   │   │ UI/UX, styling,  │
│ codebase &  │   │ code per     │   │ visual design    │
│ creates plan│   │ plan         │   │                  │
└──────────────┘   └──────────────┘   └──────────────────┘
```

---

## Installation

### Prerequisites

- **VS Code Insiders** (required for Memory feature)
- GitHub Copilot subscription
- `context7` MCP Server (used by Coder and Planner agents for live docs)

### Setup Steps

1. **Install all four agents** using the one-click badges from the Gist or run install URLs manually.
2. **Enable VS Code settings**:
   - `"Use custom agent in Subagent"` → ON
   - `"Memory"` → ON  
   *(Both in VS Code User Settings UI)*
3. **Activate**: Open VS Code Chat, select the **Orchestrator** agent, and type your request.

### Install URLs

| Agent | VS Code Install URL |
|---|---|
| Orchestrator | `vscode:chat-agent/install?url=https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/orchestrator.agent.md` |
| Planner | `vscode:chat-agent/install?url=https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/planner.agent.md` |
| Coder | `vscode:chat-agent/install?url=https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/coder.agent.md` |
| Designer | `vscode:chat-agent/install?url=https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/designer.agent.md` |

Use `vscode-insiders:` prefix instead of `vscode:` for VS Code Insiders.

---

## Agent Deep-Dive

### 1. Orchestrator (Claude Opus 4.6)

**Model:** `Claude Opus 4.6 (copilot)`  
**Tools:** `read/readFile`, `agent`, `memory`  
**Source:** [gist raw/orchestrator.agent.md](https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/orchestrator.agent.md)[^2]

The Orchestrator is the entry point for all requests. It **never writes code itself** — it only coordinates. Its execution model follows a strict four-step loop:

1. **Call Planner** with the original user request.
2. **Parse response into phases** — extract file lists per task and group tasks with no overlapping files into the same parallel phase.
3. **Execute each phase** — spawn multiple subagents simultaneously for parallel tasks; wait for phase completion before proceeding.
4. **Verify and report** — validate that all outputs integrate correctly and report to the user.

**File Conflict Prevention Strategies:**
- *Explicit File Assignment*: Each agent delegation includes an exact list of files to create/modify.
- *Sequential Phases for Overlapping Files*: If two tasks must touch the same file, they become separate sequential phases.
- *Component Boundaries*: For UI work, agents are scoped to distinct component subtrees.

**Key Constraint:** The Orchestrator describes WHAT to do, never HOW. For example:
- ✅ `"Fix the infinite loop error in SideMenu"`
- ❌ `"Fix the bug by wrapping the selector with useShallow"`

---

### 2. Planner (Claude Opus 4.6)

**Model:** `Claude Opus 4.6 (copilot)`  
**Tools:** `vscode`, `execute`, `read`, `agent`, `context7/*`, `edit`, `search`, `web`, `memory`, `todo`  
**Source:** [gist raw/planner.agent.md](https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/planner.agent.md)[^3]

The Planner **creates plans only — it never writes code.** Its workflow:

1. **Research** — search the codebase, read relevant files, identify existing patterns.
2. **Verify** — use `#context7` and `#fetch` to check live API/library documentation. No assumptions.
3. **Consider** — identify edge cases, error states, and implicit requirements.
4. **Plan** — output ordered implementation steps with file assignments.

**Output format:**
- Summary (one paragraph)
- Ordered implementation steps
- Edge cases to handle
- Open questions (if any)

The Planner explicitly uses `context7/*` tools — meaning it consults the [Context7](https://context7.com) MCP server for up-to-date library documentation before making recommendations.

---

### 3. Coder (GPT-5.3-Codex)

**Model:** `GPT-5.3-Codex (copilot)`  
**Tools:** `vscode`, `execute`, `read`, `agent`, `context7/*`, `github/*`, `edit`, `search`, `web`, `memory`, `todo`  
**Source:** [gist raw/coder.agent.md](https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/coder.agent.md)[^4]

The Coder implements code per the Planner's output. It **always calls `#context7`** before writing code for any language, framework, or library — explicitly acknowledging that training data may be stale.

**Mandatory Coding Principles:**

| Principle | Details |
|---|---|
| **Structure** | Feature-grouped layout; shared utilities minimal; framework-native composition |
| **Architecture** | Flat, explicit code; no clever abstractions; minimize coupling for regenerability |
| **Functions** | Linear control flow; small-to-medium functions; explicit state passing (no globals) |
| **Naming** | Descriptive-but-simple; comments only for invariants/assumptions |
| **Errors** | Detailed structured logs at key boundaries; explicit, informative errors |
| **Regenerability** | Any file/module must be safely rewritable without breaking the system |
| **Platform** | Use platform conventions directly (WinUI/WPF etc.) without over-abstracting |
| **Modifications** | Follow existing patterns; prefer full-file rewrites over micro-edits |
| **Quality** | Deterministic, testable behavior; simple tests on observable behavior |

---

### 4. Designer (Gemini 3.1 Pro)

**Model:** `Gemini 3.1 Pro (Preview) (copilot)`  
**Tools:** `vscode`, `execute`, `read`, `agent`, `context7/*`, `edit`, `search`, `web`, `memory`, `todo`  
**Source:** [gist raw/designer.agent.md](https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/designer.agent.md)[^5]

The Designer handles all UI/UX work. Its system prompt takes a notably assertive stance:

> *"Do not let anyone tell you how to do your job... developers have no idea what they are talking about when it comes to design, so you must take control of the design process."*

This intentionally gives the designer agent full autonomy over visual/UX decisions, prioritizing user experience over technical constraints.

---

## Companion Repository: `burkeholland/ultralight`

[GitHub: burkeholland/ultralight](https://github.com/burkeholland/ultralight)[^6]

The `ultralight` repo serves as both a live demo app and an alternative distribution point for the same agent files.

**Repository structure:**

```
ultralight/
├── agents/
│   ├── orchestrator.agent.md   ← same as Gist
│   ├── planner.agent.md        ← same as Gist
│   ├── coder.agent.md          ← same as Gist
│   └── designer.agent.md       ← same as Gist
├── index.html
├── style.css
├── plugin.json
├── .mcp.json
└── README.md
```

The `.mcp.json` and `plugin.json` configure MCP server connections used by the agents during demo sessions.[^7]

---

## Execution Example: "Add dark mode to the app"

```
Step 1 — Orchestrator calls Planner:
  "Create an implementation plan for adding dark mode support"

Step 2 — Planner returns phases. Orchestrator parses:
  Phase 1: Design (no dependencies)
    Task 1.1 → Designer: Create dark mode color palette + theme tokens
    Task 1.2 → Designer: Design toggle UI component
  
  Phase 2: Core Implementation (depends on Phase 1)
    Task 2.1 → Coder: Implement theme context + persistence
              Files: src/contexts/ThemeContext.tsx, src/hooks/useTheme.ts
    Task 2.2 → Coder: Create toggle component
              Files: src/components/ThemeToggle.tsx
    (No file overlap → PARALLEL)
  
  Phase 3: Apply Theme (depends on Phase 2)
    Task 3.1 → Coder: Update all components to use theme tokens

Step 3 — Execute:
  Phase 1: Call Designer for both tasks in parallel
  Phase 2: Call Coder twice in parallel (different files)
  Phase 3: Call Coder once sequentially

Step 4 — Orchestrator validates and reports to user.
```

---

## Relevance to Blaze.LlmGateway / CodebrewRouter

The Ultralight Orchestration pattern is directly relevant to the `Blaze.LlmGateway` project:

1. **Multi-model routing**: The system explicitly routes different task types to different LLM backends (Claude for orchestration/planning, Codex for code, Gemini for design) — exactly the pattern that `OllamaMetaRoutingStrategy` and `KeywordRoutingStrategy` implement in this project.

2. **Agent orchestration pattern**: The phase-based parallel execution model in the Orchestrator agent is a practical template for designing multi-agent workflows that could be supported via the gateway's routing layer.

3. **Context7 MCP integration**: Both the Planner and Coder agents use `context7/*` tools — a reminder that MCP tool injection (via `McpToolDelegatingClient`) is critical to agentic AI workflows and supports the ongoing work to fully wire `McpConnectionManager`.

---

## Key Repositories Summary

| Repository | Purpose | Key Files |
|---|---|---|
| [gist:burkeholland/0e68481f96e94bbb98134fa6efd00436](https://gist.github.com/burkeholland/0e68481f96e94bbb98134fa6efd00436) | Installation guide + agent definitions (ainstall.md) | `orchestrator.agent.md`, `planner.agent.md`, `coder.agent.md`, `designer.agent.md` |
| [burkeholland/ultralight](https://github.com/burkeholland/ultralight) | Demo app + mirrored agent files | `agents/*.agent.md`, `index.html`, `.mcp.json` |

---

## Confidence Assessment

| Claim | Confidence | Basis |
|---|---|---|
| Gist ID and URL | ✅ High | Directly fetched raw content |
| Agent system prompts | ✅ High | Fetched verbatim from Gist raw URLs |
| Model assignments | ✅ High | From YAML frontmatter of each agent file |
| Ultralight repo structure | ✅ High | Directly queried GitHub API |
| Context7 MCP dependency | ✅ High | Explicitly in planner and coder agent prompts |
| Relationship to main `ainstall.md` document | ✅ High | Web search confirmed, Gist fetched and verified |

---

## Footnotes

[^1]: [burkeholland/ultralight](https://github.com/burkeholland/ultralight) — companion demo repo, updated 2026-04-12, contains `agents/` directory with identical agent files.

[^2]: Gist `orchestrator.agent.md` raw: https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/orchestrator.agent.md

[^3]: Gist `planner.agent.md` raw: https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/planner.agent.md

[^4]: Gist `coder.agent.md` raw: https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/coder.agent.md

[^5]: Gist `designer.agent.md` raw: https://gist.githubusercontent.com/burkeholland/0e68481f96e94bbb98134fa6efd00436/raw/designer.agent.md

[^6]: [burkeholland/ultralight](https://github.com/burkeholland/ultralight) — 18 stars, active as of 2026-04-12, TypeScript/HTML/CSS demo app.

[^7]: `ultralight/.mcp.json` and `ultralight/plugin.json` — 120 bytes and 354 bytes respectively, configure MCP server tooling used during agent sessions.
