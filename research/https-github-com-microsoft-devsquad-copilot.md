# DevSquad Copilot (`microsoft/devsquad-copilot`) and `microsoft.github.io/devsquad-copilot` — technical research report

**Repository:** [microsoft/devsquad-copilot](https://github.com/microsoft/devsquad-copilot)  
**Published docs:** [microsoft.github.io/devsquad-copilot](https://microsoft.github.io/devsquad-copilot/)  
**Snapshot analyzed:** `8186b6fcf1c989ac77f4980d06679c5585720d69`  
**Research basis:** local mirror under `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo`.

## Executive Summary

DevSquad Copilot is best understood as a **delivery framework for GitHub Copilot**, not as a conventional library or SDK. Its core promise is to move teams from **intent to implementation** through persisted artifacts such as envisioning docs, specs, ADRs, plans, task lists, and review outputs, with explicit checkpoints between phases.[^1][^2]

Technically, the product is implemented as a **Copilot plugin plus documentation site**. The plugin manifests point to a packaged `devsquad` plugin that ships agents, skills, hooks, and MCP server configuration, while the docs site is an Astro/Starlight project published to GitHub Pages under `/devsquad-copilot/`.[^3][^4][^5]

The architectural center is a **conductor pattern**: the user can enter through the `devsquad` conductor, which delegates to specialist agents, and some specialists (`plan`, `implement`, `review`, `refine`) further delegate to worker sub-agents with isolated context. Cross-phase state is intentionally reconstructed from disk artifacts and handoff envelopes rather than inherited implicitly from chat history.[^6][^7][^8]

Operationally, the repository is still **pre-1.0 and evolving quickly**. Recent releases added nested sub-agent execution, deterministic project initialization via shell script, tool-extension overlays, a debugging-recovery skill, and compatibility fixes for current Copilot CLI MCP configuration rules.[^9][^10][^11]

## Architecture/System Overview

At a system level, DevSquad combines five main surfaces:

| Surface | Purpose | Key local evidence |
| --- | --- | --- |
| Plugin manifests | Declare the installable Copilot plugin and its packaged assets | `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugin\plugin.json`, `...\plugins\devsquad\.github\plugin\plugin.json`[^3] |
| Agent definitions | Define conductor, lifecycle agents, support agents, and worker sub-agents | `...\plugins\devsquad\agents\*.agent.md`[^6][^12] |
| Knowledge and guardrails | Skills, instructions, hooks, and MCP server config | `...\plugins\devsquad\skills\`, `...\instructions\`, `...\hooks\`, `...\plugins\devsquad\.mcp.json`[^13][^14][^15][^16] |
| Docs site | End-user documentation and conceptual architecture | `...\docs\src\content\docs\*.mdx`, `...\docs\astro.config.mjs`[^4][^5] |
| Repo automation | Deploy docs and enforce contribution policy | `...\workflows\pages.yml`, `...\workflows\block-pr.yml`, `...\workflows\auto-close-pr.yml`[^17][^18] |

The resulting architecture looks like this:

```text
┌──────────────┐
│ Developer    │
└──────┬───────┘
       │
       ▼
┌──────────────────────┐
│ devsquad conductor   │
│ routes intent only   │
└──────┬───────────────┘
       │ delegates by phase
       ▼
┌───────────────────────────────────────────────────────────────┐
│ Specialist agents                                             │
│ init | envision | kickoff | specify | plan | decompose       │
│ implement | review | security | sprint | refine | extend     │
└──────┬────────────────────────────────────────────────────────┘
       │ nested delegation for complex phases
       ▼
┌───────────────────────────────────────────────────────────────┐
│ Worker agents                                                 │
│ plan: context / architecture / design                         │
│ implement: validate / execute / verify / finalize             │
│ review: spec / adr / code / security / tests                  │
│ refine: artifacts / health                                    │
└──────┬────────────────────────────────────────────────────────┘
       │ use tools / read artifacts
       ├───────────────► MCP servers (GitHub, ADO, Azure, Learn, Draw.io)
       └───────────────► Disk artifacts (spec.md, plan.md, ADRs, tasks.md, PRs)
```

That diagram is not just conceptual documentation; it matches the actual packaged topology. The conductor manifest lists 12 specialist agents, while the `plan`, `implement`, and `review` agents explicitly declare worker agents in frontmatter, and the docs mirror the same conductor/coordinator-worker model.[^6][^7][^12]

## Delivery Model and Artifact Chain

The framework organizes work as a structured pipeline: **envision → specify → plan → decompose → implement → review**, with initialization and refinement/sprint/security support around that flow. The lifecycle documentation maps each phase to persistent artifacts such as `docs/envisioning/README.md`, `docs/features/*/spec.md`, `plan.md`, ADRs, `tasks.md`, source code, and review logs.[^1][^2][^19]

One of DevSquad’s strongest design choices is that it treats **artifacts on disk as the source of truth**. The framework docs and ADR 0003 both state that phases should start with clean context, reread approved artifacts from disk, and pass only a compact handoff envelope forward. Board systems are authoritative for task status and ownership, while repo artifacts are authoritative for design and requirements.[^8][^20][^21]

That means DevSquad is opinionated about process, but not about application stack. The framework page explicitly describes it as programming-language-agnostic and AI-model-agnostic; the guidance lives in extensibility mechanisms rather than hard-coded language runtimes.[^7]

## Core Runtime Model

### 1. Conductor and dual-mode specialists

The `devsquad` agent is intentionally thin. Its own instructions say it should detect state and intent, invoke sub-agents, relay questions, execute structured actions, and maintain cross-phase context, but **must not generate specs, ADRs, or code directly**.[^6]

This is backed by ADR 0001, which rejects both a monolithic orchestrator and a purely manual “pick the right agent yourself” approach in favor of a **mediated coordinator-worker pattern**. The main trade-off accepted by the ADR is higher token and latency overhead in exchange for guided mode plus direct specialist fallback when the conductor is not appropriate.[^22]

ADR 0002 explains how the dual-mode protocol works. When the conductor invokes a specialist, it prefixes the request with `[CONDUCTOR]`, and the specialist returns structured tags like `[ASK]`, `[CREATE]`, `[EDIT]`, `[BOARD]`, `[CHECKPOINT]`, and `[DONE]`; without that prefix, the same specialist works in direct user-facing mode.[^23] That same protocol is reflected in the actual agent instructions for `plan`, `implement`, and `review`.[^12]

### 2. Coordinator-worker decomposition

The public docs describe 13 user-visible agents: the `devsquad` conductor plus 12 specialist agents.[^24] Under the hood, the packaged plugin contains many more `.agent.md` files because several specialists are now coordinators with worker sub-agents.[^9][^25]

The most important coordinator patterns are:

1. **`devsquad.plan`**: delegates context loading, systemic architecture analysis, and design artifact generation to `plan.context`, `plan.architecture`, and `plan.design`, and can also invoke `devsquad.security` when planning reveals security-sensitive decisions.[^12][^19]
2. **`devsquad.implement`**: orchestrates validation, execution, verification, nested review, and finalization through `implement.validate`, `implement.execute`, `implement.verify`, `review`, and `implement.finalize`.[^26][^27]
3. **`devsquad.review`**: fans out to five parallel workers for spec, ADR, code, security, and tests checks, then merges and classifies findings.[^28][^29]
4. **`devsquad.refine`**: splits artifact analysis and backlog-health checks into isolated workers.[^7][^9]

This coordinator-worker structure is one of the clearest signs that DevSquad is a workflow product more than a “single agent.” The repo changelog shows the nested-subagent architecture becoming a first-class design in v0.7.0, with dedicated workers added for plan, implement, review, and refine.[^9]

### 3. Context, reasoning, and review independence

The `reasoning` skill formalizes two cross-cutting mechanisms: a **Reasoning Log** that records non-trivial decisions plus confidence levels, and a **Handoff Envelope** that passes only relevant artifacts, inherited assumptions, pending decisions, and discarded context to downstream agents.[^21]

This is paired with deliberate **clean-context review**. The review agent instructions explicitly state that validation is strongest when performed by an agent that did not implement the code, and the framework docs describe `implement → review` as a hard cleanup boundary where the reviewer rereads artifacts independently.[^20][^28]

## Guardrails and Extension Mechanisms

DevSquad’s main implementation idea is that not all knowledge should live in the same place. ADR 0004 and the docs split knowledge across **instructions**, **skills**, **agents**, **hooks**, and **MCP servers**, each with a different activation model and context cost.[^13][^30]

### Instructions

Instructions are deterministic, path-scoped rules. The docs list seven of them, covering feature specs, ADRs, tasks, envisioning docs, migration specs, migration tasks, and general documentation style.[^13] The repo’s own `copilot-instructions.md` also documents the same structure and positions instructions as concise artifact-type-specific rules rather than general documentation.[^14]

### Skills

Skills are semantic, description-triggered knowledge packages. The current skills reference enumerates **19** built-in skills, including `reasoning`, `quality-gate`, `security-review`, `engineering-practices`, `work-item-creation`, and the newer `debugging-recovery` skill.[^15] The v0.8.0 changelog confirms `debugging-recovery` was added recently, which explains why some other docs still say 18 skills.[^10]

That mismatch is a real documentation-drift finding:

| Surface | Skill count stated |
| --- | --- |
| `docs/src/content/docs/skills.mdx` | 19[^15] |
| `docs/src/content/docs/framework.mdx` | 18[^7] |
| `.github/plugins/devsquad/README.md` | 18[^31] |

### Hooks

Hooks are external bash scripts with “zero LLM context cost.” The docs describe five built-in hooks, and the shipped hook configs register three `sessionStart` checks and two `postToolUse` validations.[^32][^33]

The scripts themselves are pragmatic:

1. `detect-branching-strategy.sh` inspects remote branches, classifies the repo as GitFlow or trunk-based, and writes `.memory/git-config.md` with confidence metadata.[^34]
2. `detect-tool-extensions.sh` warns when consumer tool-extension YAMLs are present but unsynced, changed, or out of date with the current plugin version.[^35]
3. `sdd-init.sh` replaces LLM-driven template regeneration with deterministic verification, diffing, creation, and update commands over a manifest of framework-managed files.[^36]

### MCP servers

The shipped `.mcp.json` config declares five MCP servers: GitHub, Azure DevOps, Azure, Microsoft Learn, and Draw.io.[^16] The docs map those same servers to concrete agent use cases: GitHub and Azure DevOps for boards and repos, Azure and Learn for architecture guidance, and Draw.io for diagrams.[^32]

### Tool extensions

Tool extensions are one of the more interesting newer capabilities. The extensibility docs describe them as YAML patches that inject extra MCP tools into existing plugin agents, and the `sync-tool-extensions.sh` hook implements that by discovering the installed plugin location, merging frontmatter tools, appending extension instructions, and generating workspace agent overrides under `.github/agents/`.[^11][^37]

This feature is explicitly framed as **consumer-side customization without modifying the plugin source**, which fits the broader DevSquad design philosophy of preserving a stable core while letting downstream repos add stack-specific behavior.[^11][^37]

## Planning, Implementation, and Review Internals

### Planning

`devsquad.plan` is more rigorous than “write a plan.md.” Its instructions require it to load context, detect whether the target is a feature or migration, stop on missing or conflicting ADRs, ask before deciding, and only create design artifacts after user approval.[^12] The lifecycle docs summarize the same phase as a Socratic planning step that can create ADRs, consult Microsoft Learn, estimate Azure costs, and escalate security-sensitive decisions into `devsquad.security`.[^19]

### Implementation

`devsquad.implement` validates the request, classifies impact, runs understanding checkpoints for medium/high impact changes, reads plan/spec/ADRs, and then orchestrates branch management, execution, verification, review, PR finalization, and next-task suggestions.[^26]

The `implement.execute` worker is where the repo’s operational discipline is clearest. It requires:

1. **Prove-It bug-fix flow**: reproduce, write failing test, confirm the failure is for the right reason, implement the minimal fix, and rerun the full suite.[^27]
2. **Save-point protocol**: commit after each passing task or parallel task group and revert to the last committed state before debugging further failures.[^27]
3. **Scope reporting**: explicit “Changes made,” “Not touched (intentionally),” and “Potential concerns” sections in worker output.[^27]
4. **Version/source verification**: confirm Microsoft APIs against official docs, and verify version-sensitive non-Microsoft APIs against official sources before leaning on them.[^27]

### Review

`devsquad.review` is explicitly designed as an independent validator. It collects changed files, usages, and IDE problems; builds a checklist from spec, plan, ADRs, and coding guidelines; runs five review workers in parallel; then merges findings into a severity-ranked review log.[^28]

The `review.code` worker adds a specific “AI code smell” layer by looking for duplicate blocks, missing abstractions, unjustified dependencies, unguarded external calls, and unnecessary complexity, while also applying **Chesterton’s Fence** before recommending removal or simplification.[^38]

Together, `implement` and `review` show that DevSquad’s real product value is not code generation by itself; it is the set of process constraints wrapped around code generation.[^26][^28]

## Documentation Site and Deployment

The published site is not a separate product line; it is built from the same repo’s `docs/` directory. `astro.config.mjs` sets `site: 'https://microsoft.github.io'` and `base: '/devsquad-copilot/'`, uses Starlight plus Mermaid, and defines the left-nav structure for Getting Started, Architecture, Agents, Skills, Components, Extensibility, and Reference pages.[^4]

The docs home page content in `docs/src/content/docs/index.mdx` matches the live site framing: “From Intent to Implementation,” the four-layer artifact chain (Intent / Specification / Architecture Decisions / Delivery), and the “Intentional AI” principles of asking before assuming, traceable decisions, human control, and comprehension over speed.[^5]

Deployment is automated through `pages.yml`, which triggers on pushes to `main` that touch `docs/**`, installs dependencies in the `docs` directory, builds with Astro on Node 22, and deploys the generated `docs/dist` artifact to GitHub Pages.[^17]

The repo changelog also records a docs-site consolidation in v0.7.1: most former `docs/framework/` narrative content was removed from the legacy repo docs tree because the new docs site became the primary documentation surface, while ADRs and images stayed in `docs/framework/`.[^39]

## Installation, Usage, and Contribution Notes

The getting-started flow has two primary entry modes:

1. **VS Code**: VS Code 1.113.0+, GitHub Copilot Chat, Node.js 18+, and nested sub-agent support enabled through `chat.subagents.allowInvocationsFromSubagents`.[^40]
2. **GitHub Copilot CLI**: Copilot CLI 1.0.6+, authenticated GitHub account, Node.js 18+, and plugin installation via `copilot plugin marketplace add microsoft/devsquad-copilot` followed by `copilot plugin install devsquad@devsquad-copilot`.[^40]

The v0.8.1 changelog shows that the CLI install docs were updated because the older direct install form is now deprecated, and the plugin’s `.mcp.json` had to switch to the `mcpServers` key to stay compatible with newer CLI schema expectations.[^41]

On the repo-governance side, this project also enforces a **vouch-based contribution gate**. One workflow blocks unvouched pull requests by checking `.github/VOUCHED.md`, and another comments on and auto-closes PRs from authors who are not vouched contributors unless they are owners, members, collaborators, or contributors.[^18]

## Notable Consistency Findings

Two repository details are worth calling out for anyone evaluating or adopting the project:

1. **Skill-count drift in docs**: the current skills catalog lists 19 skills, but some higher-level pages and the plugin README still say 18, which indicates documentation lag rather than a different packaging model.[^7][^15][^31]
2. **Manifest-version drift**: the root plugin manifest and the self-contained plugin manifest are both at `0.8.1`, but `.github/plugin/marketplace.json` still advertises the packaged plugin as `0.8.0`.[^3][^42]

Neither issue changes the core architecture, but both are useful signals that the project is still moving quickly and that packaging/docs metadata can lag behind the current agent/skill set.[^9][^10]

## Confidence Assessment

**High confidence**

- The overall product shape: DevSquad is a Copilot delivery framework implemented through plugin manifests, agent/skill/hook definitions, and a docs site.[^1][^3][^4]
- The conductor/coordinator-worker architecture, structured-action protocol, and artifact-based context model are explicit in both ADRs and the shipped agent files.[^6][^8][^12][^22][^23]
- The docs-site build/deploy path, plugin prerequisites, installed MCP servers, and recent release history are directly visible in versioned files.[^4][^16][^17][^40][^41]

**Medium confidence / inferred from repo content**

- I did not execute the plugin in VS Code or Copilot CLI, so statements about runtime behavior are based on the packaged instructions, docs, and shell scripts rather than live end-to-end execution.
- I treat the repo’s agent Markdown plus hook scripts as the framework’s “implementation,” because there is very little conventional application source code here; that interpretation is strongly supported by the manifests and structure docs, but it is still an architectural reading of the project rather than a binary/runtime inspection.[^3][^14]

## Footnotes

[^1]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\README.md:3-26`
[^2]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\how-it-works.mdx:8-42`
[^3]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugin\plugin.json:1-16`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\.github\plugin\plugin.json:1-16`
[^4]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\astro.config.mjs:6-144`
[^5]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\index.mdx:29-108`
[^6]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.agent.md:8-166`
[^7]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\framework.mdx:36-199`
[^8]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\core-components\context-management.mdx:11-123`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\framework\decisions\0003-context-management.md:39-77`
[^9]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\CHANGELOG.md:48-72`
[^10]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\CHANGELOG.md:15-32`
[^11]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\CHANGELOG.md:144-160`
[^12]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.plan.agent.md:1-247`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.implement.agent.md:1-260`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.review.agent.md:1-260`
[^13]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\core-components\instructions.mdx:11-116`
[^14]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\copilot-instructions.md:72-183`
[^15]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\skills.mdx:11-49`
[^16]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\.mcp.json:1-32`
[^17]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\workflows\pages.yml:1-60`
[^18]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\workflows\block-pr.yml:1-35`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\workflows\auto-close-pr.yml:1-72`
[^19]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\agents\lifecycle.mdx:11-179`
[^20]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\core-components\context-management.mdx:64-123`
[^21]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\skills\reasoning\SKILL.md:10-126`
[^22]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\framework\decisions\0001-agent-orchestration.md:6-95`
[^23]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\framework\decisions\0002-conductor-sub-agent-communication.md:6-126`
[^24]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\agents\overview.mdx:11-78`
[^25]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\agents\support.mdx:25-37`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\framework.mdx:87-139`
[^26]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.implement.agent.md:116-259`
[^27]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.implement.execute.agent.md:8-125`
[^28]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.review.agent.md:40-259`
[^29]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\agents\support.mdx:15-37`
[^30]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\framework\decisions\0004-activation-model.md:6-92`
[^31]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\README.md:7-53`
[^32]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\core-components\hooks.mdx:11-65`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\core-components\mcp-servers.mdx:11-79`
[^33]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\hooks\hooks.json:1-35`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\hooks\hooks.json:1-35`
[^34]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\hooks\detect-branching-strategy.sh:1-60`
[^35]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\hooks\detect-tool-extensions.sh:1-81`
[^36]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\hooks\sdd-init.sh:1-256`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\CHANGELOG.md:76-92`
[^37]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\extensibility.mdx:17-199`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\hooks\sync-tool-extensions.sh:1-320`
[^38]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugins\devsquad\agents\devsquad.review.code.agent.md:8-74`
[^39]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\CHANGELOG.md:39-47`
[^40]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\docs\src\content\docs\getting-started.mdx:15-128`
[^41]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\CHANGELOG.md:8-14`
[^42]: `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugin\marketplace.json:10-17`; `E:\src\CodebrewRouter\research\sources\microsoft-devsquad-copilot\repo\.github\plugin\plugin.json:1-16`
