# Microsoft Work IQ (`microsoft/work-iq`) — technical research report

## Executive Summary

[Microsoft Work IQ](https://github.com/microsoft/work-iq) is best understood as a **plugin marketplace and prompt-packaging repo**, not as the full implementation repo for the Work IQ MCP server itself. The checked-in repository centers on marketplace manifests, plugin metadata, skill definitions, admin documentation, and tenant bootstrap scripts for GitHub Copilot CLI and adjacent agent ecosystems.[^1][^2]

The repo publishes three installable plugins: `workiq`, `microsoft-365-agents-toolkit`, and `workiq-productivity`. Only the `workiq` plugin declares an MCP server launch configuration; the other two primarily package skill instructions and reference docs that tell an agent when and how to use Work IQ or the Microsoft 365 Agents Toolkit CLI.[^2][^3][^8]

The actual Work IQ runtime distributed to users is pulled from the npm package `@microsoft/workiq` over stdio transport, while the root `server.json` advertises that package through an MCP registry-style manifest. The docs also state that Work IQ uses the Microsoft 365 Copilot Chat API backend and requires tenant-level delegated permissions before users can query workplace data.[^3][^10]

The most substantive executable logic in this repo lives in the PowerShell enablement scripts. Those scripts provision Microsoft Entra service principals for Work IQ-related MCP resources and create or validate OAuth permission grants for Microsoft Graph and the Work IQ Tools resource, which makes the repo as much an **enterprise enablement package** as a plugin catalog.[^10][^11]

## Architecture / System Overview

At a high level, the repository has two distinct planes: a **distribution plane** that tells Copilot/Claude how to discover and install plugins, and a **behavior plane** that tells the agent what to do once those plugins are installed.[^2][^8][^12]

```text
User prompt
   |
   v
Copilot CLI / agent host
   |
   +--> Marketplace registry
   |      +--> .github/plugin/marketplace.json
   |      +--> .claude-plugin/marketplace.json
   |
   +--> Installed plugin
          |
          +--> workiq
          |      +--> .mcp.json
          |      +--> npx -y @microsoft/workiq mcp
          |      +--> ask_work_iq / accept_eula / get_debug_link
          |
          +--> workiq-productivity
          |      +--> recipe-style skills
          |      +--> orchestrate repeated ask_work_iq queries
          |
          +--> microsoft-365-agents-toolkit
                 +--> ATK CLI workflows
                 +--> declarative agent rules
                 +--> MCP widget / protocol guidance

Tenant admin path
   |
   +--> ADMIN-INSTRUCTIONS.md
   +--> Verify-WorkIQTenant.ps1
   +--> Enable-WorkIQToolsForTenant.ps1
   +--> Entra service principals + delegated permission grants
```

That split matters because this repo does **not** expose the inner implementation of the `@microsoft/workiq` package in the way a normal app repo would. Instead, it defines how clients install it, how agents invoke it, and how tenant admins unblock the permissions it needs to reach Microsoft 365 Copilot and Graph-backed organizational data.[^3][^4][^10][^11]

Another notable design choice is that the repo targets more than one host ecosystem. GitHub Copilot CLI uses `.github/plugin/marketplace.json`, while a parallel `.claude-plugin/marketplace.json` publishes the same logical plugin collection with different relative skill paths, showing that Work IQ is being packaged as reusable agent infrastructure rather than a single-tool integration.[^2]

## Key Subtrees Summary

| Subtree | Role | Evidence |
|---|---|---|
| [`microsoft/work-iq`](https://github.com/microsoft/work-iq) root | Top-level marketplace, install docs, admin guidance, MCP server manifest | Root README, AGENTS, `server.json`, admin docs[^1][^3][^10] |
| `plugins/workiq` | Thin adapter that exposes the external `@microsoft/workiq` MCP server and a single high-level WorkIQ skill | `.mcp.json`, plugin README, `workiq` skill[^3][^4] |
| `plugins/workiq-productivity` | Read-only recipe pack built on top of Work IQ queries for email, meetings, Teams, Planner, SharePoint, and org data | Plugin README and nine skills[^5][^6][^7] |
| `plugins/microsoft-365-agents-toolkit` | Prescriptive guidance pack for ATK CLI usage, declarative-agent editing, and Copilot widget / MCP server integration | Plugin README, three skills, reference docs[^8][^9] |
| `scripts/` + `ADMIN-INSTRUCTIONS.md` | Tenant bootstrap and verification for app registrations and permission grants | Admin guide plus enable/verify PowerShell scripts[^10][^11] |

## 1. Marketplace and packaging model

The root README positions Work IQ as "the official Microsoft Work IQ plugin collection for GitHub Copilot," with one-time marketplace registration via `/plugin marketplace add microsoft/work-iq`, followed by installation of individual plugins. The same docs also support a standalone MCP installation path, which means the repo is serving both as a Copilot marketplace entry point and as a registry-style descriptor for generic MCP clients.[^1][^3]

The root marketplace manifest currently registers exactly three plugins with human-readable descriptions and skill lists. A parallel Claude-oriented marketplace file mirrors the same three plugins, which implies the repo is intentionally curating the same capabilities for multiple agent runtimes rather than hard-coding them to GitHub Copilot alone.[^2]

Operationally, plugin authoring is lightweight and contract-driven: `CONTRIBUTING.md` requires a plugin directory, `README.md`, `SKILL.md`, optional `.mcp.json`, and registration in `.github/plugin/marketplace.json`. `AGENTS.md` adds an important runtime constraint: Copilot CLI silently drops skills whose YAML `description` exceeds 1024 characters, and after changing a plugin or skill the maintainer is expected to reinstall that plugin so the running session picks up the new metadata.[^12]

## 2. The `workiq` plugin: thin wrapper around the external MCP server

`plugins/workiq/.mcp.json` configures a single MCP server named `workiq` that launches through `npx -y @microsoft/workiq@latest mcp`, while the root `server.json` advertises the same package as `@microsoft/workiq` version `0.2.8` over stdio transport. The plugin README repeats that model and documents both global npm install and ephemeral `npx` execution, which makes it clear that the checked-in repo delegates runtime behavior to the published npm package rather than shipping server code here.[^3]

The primary skill surface is intentionally minimal. `plugins/workiq/skills/workiq/SKILL.md` defines one aggressive routing rule: for essentially any workplace-context question, the agent should try `ask_work_iq` first rather than claiming it lacks access to email, meetings, messages, documents, or org context. The documented MCP interface is a single `question` string, and the catalog says the exposed tool set for the server is `ask_work_iq`, `accept_eula`, and `get_debug_link`.[^4]

Conceptually, that means Work IQ is being used as a **semantic query broker** over Microsoft 365 workplace data. The skill examples span email, meetings, Teams, documents, expertise lookup, project status, and organizational priorities, but the actual host-facing interface stays deliberately small: one general-purpose question tool, plus EULA/debug helpers.[^4]

## 3. `workiq-productivity`: recipe pack over `ask_work_iq`

The `workiq-productivity` plugin is explicitly described as a set of **nine read-only skills** powered by the local WorkIQ CLI. Its README and marketplace metadata frame it as an analytics/orchestration layer rather than a separate backend: the plugin repackages Work IQ into higher-level workflows for inbox triage, meeting cost analysis, org charts, cross-plan Planner search, SharePoint browsing, Teams channel auditing, and channel digests.[^2][^5]

The first cluster of skills focuses on personal productivity analytics over email and meetings. `action-item-extractor` pulls a meeting, attendee list, and Teams chat, then parses assignments, deadlines, and urgency markers into a structured action table; `daily-outlook-triage` combines profile lookup, inbox retrieval, and calendar lookup into a day summary; `email-analytics` computes volume, unread, sender, and temporal metrics over a time window; and `meeting-cost-calculator` turns calendar events into total meeting hours, attendee-hours, daily load charts, and overload heuristics.[^6]

The second cluster extends Work IQ into organizational navigation and collaboration hygiene. `org-chart` recursively resolves a person's manager chain and direct reports to render an ASCII org tree; `multi-plan-search` searches Planner tasks across all accessible plans with assignee, status, due date, and keyword filters; `site-explorer` walks SharePoint sites, lists, libraries, folders, and files; `channel-audit` classifies Teams channels as active, slow, stale, dead, or bot-only; and `channel-digest` synthesizes messages across channels into decisions, action items, announcements, and @mentions.[^7]

Architecturally, the important point is that these are **prompt recipes, not new APIs**. Each skill repeatedly instructs the host to call the same underlying Work IQ tool (`ask_work_iq`) with richer natural-language prompts, then perform client-side aggregation, classification, formatting, and prioritization on the returned data.[^6][^7]

## 4. `microsoft-365-agents-toolkit`: prescriptive agent-development workflows

The `microsoft-365-agents-toolkit` plugin is a separate lane from Work IQ data access. Its README says it exists to build Microsoft 365 Copilot declarative agents, and its three skills split into bootstrap (`install-atk`), declarative-agent lifecycle management (`declarative-agent-developer`), and MCP/widget-oriented development (`ui-widget-developer`).[^8]

`install-atk` is straightforward but opinionated: it standardizes on `npx -y --package @microsoft/m365agentstoolkit-cli atk` rather than global installs, optionally installing the VS Code extension via the published extension ID. That pattern mirrors the Work IQ plugin's npx-first approach and keeps the repo aligned to package-driven distribution rather than checked-in binaries.[^8]

`declarative-agent-developer` is the most policy-heavy file in the repo. It forces an initial workspace fingerprinting step, rejects non-agent workspaces, forbids creating `declarativeAgent.json` manually, refuses deployment when manifest errors exist, requires schema-version checks before adding features, insists on using exact ATK CLI commands for scaffolding and action/plugin creation, and mandates deployment plus a test link after edits unless the user explicitly opts out.[^8]

That skill is backed by an unusually deep reference corpus. The scaffolding workflow says the only valid bootstrap command is `atk new -c declarative-agent -with-plugin no -i false`, explicitly forbids nonexistent commands like `atk init` or `atk scaffold`, and requires adding AGENTS/CLAUDE context files so future agents invoke the right skill automatically. On the widget side, the protocol references define the MCP transport contract, CORS rules, `resources`/`tools` capability declaration, widget resource registration, output template wiring, and the required "widget-resource-tool triplet" that makes Copilot widget rendering work.[^9]

`ui-widget-developer` goes even further into runtime discipline. It forces a path decision between OpenAI Apps and MCP Apps, requires detached OS processes for local servers and dev tunnels, forbids hand-written `mcpPlugin.json` tool definitions, and requires MCP Inspector-based discovery plus end-to-end verification through the tunnel before provisioning the agent. In other words, this plugin is not generic documentation; it is a codified operational playbook for how Microsoft wants Copilot-widget MCP servers to be built and wired.[^8][^9]

## 5. Tenant enablement and permission model

The admin story is central to Work IQ, not a footnote. `ADMIN-INSTRUCTIONS.md` says Work IQ uses the Microsoft 365 Copilot Chat API backend and requires delegated permissions such as `Sites.Read.All`, `Mail.Read`, `People.Read.All`, `OnlineMeetingTranscript.Read.All`, `Chat.Read`, `ChannelMessage.Read.All`, and `ExternalItem.Read.All`. The same guide also says users need Microsoft 365 Copilot licenses and that tenant admins may need to configure access, Conditional Access, and audit logging before rollout.[^10]

The docs also acknowledge a real provisioning failure mode: the "quick start" admin-consent URL can fail with `AADSTS650052` because the Work IQ Tools MCP Server resource service principal may not yet exist in the tenant. The recommended mitigation is either the read-only verification script or the full enablement script, which is a strong signal that Work IQ adoption depends on Entra app-registration plumbing as much as on local plugin installation.[^10]

The PowerShell scripts implement that plumbing explicitly. `Enable-WorkIQToolsForTenant.ps1` installs Microsoft.Graph modules if needed, connects with `Application.ReadWrite.All` and `DelegatedPermissionGrant.ReadWrite.All`, provisions the Work IQ CLI service principal plus ten MCP-related resource service principals, grants the required Microsoft Graph delegated scopes, and grants an explicit set of Work IQ Tools scopes such as `McpServers.CopilotMCP.All`, `McpServers.Mail.All`, `McpServers.Calendar.All`, `McpServers.Teams.All`, `McpServers.OneDrive.All`, and admin/management scopes. `Verify-WorkIQTenant.ps1` performs the complementary read-only audit and flags missing service principals or incomplete grants.[^11]

This makes the repo's real architecture clearer: Work IQ is packaged to feel simple for end users, but underneath it depends on a fairly heavy enterprise trust setup. The repo documents and automates that trust bootstrap directly because without it the `ask_work_iq` experience cannot access tenant data reliably.[^10][^11]

## 6. Notable caveats and integration risks

The first caveat is repository scope. Based on the checked-in artifacts and the launch manifests, this repo should be treated as the **distribution and orchestration layer** for Work IQ rather than the source of truth for the `@microsoft/workiq` runtime implementation. If you want to understand the internals of the actual CLI/MCP server, this repo points outward to the npm package and to a separate GitHub URL (`microsoft/work-iq-mcp`) in `server.json` and admin documentation.[^3][^13]

The second caveat is metadata drift. The main marketplace manifest lists `microsoft-365-agents-toolkit` at version `1.1.1`, while the plugin's own `.github/plugin/plugin.json` reports version `1.2.1`. That may be harmless, but it is a concrete sign that version coordination across marketplace-level and plugin-level metadata is not perfectly unified.[^13]

The third caveat is that much of the repo's "logic" is encoded as skill instructions rather than executable code. That is powerful for agent orchestration, but it also means correctness depends on prompt adherence, host behavior, and the external runtime packages or services those prompts call into. In other words, the repo's maintainability risk is more about **instruction quality and packaging consistency** than about algorithmic bugs in local application code.[^8][^12]

## Confidence Assessment

**High confidence** on the repo's role, packaging model, plugin inventory, admin-permission workflow, and the distinction between marketplace metadata versus runtime package delivery. Those findings are directly grounded in the repository's manifests, skill definitions, admin guide, and PowerShell scripts rather than external summaries.[^1][^2][^3][^10][^11]

**Medium confidence** on the precise relationship between `microsoft/work-iq` and `microsoft/work-iq-mcp`. The repo clearly references the latter in `server.json` and admin docs, but this repository snapshot does not itself explain whether that is a renamed upstream, a runtime-only source repo, or simply a stale metadata pointer.[^3][^13]

**Low uncertainty overall** on the high-level conclusions, but any claim about current package versions, preview behavior, or required permissions should be read as a snapshot of commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`, because this repo is explicitly marked Public Preview and several moving parts are external to the checked-in tree.[^1][^3]

## Footnotes

[^1]: `microsoft/work-iq/README.md:1-13`, `microsoft/work-iq/README.md:15-28`, `microsoft/work-iq/README.md:32-45`, `microsoft/work-iq/README.md:57-106`, `microsoft/work-iq/README.md:168-203`, `microsoft/work-iq/AGENTS.md:1-4`, `microsoft/work-iq/AGENTS.md:23-35` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^2]: `microsoft/work-iq/.github/plugin/marketplace.json:1-49`, `microsoft/work-iq/.claude-plugin/marketplace.json:1-49`, `microsoft/work-iq/PLUGINS.md:74-171` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^3]: `microsoft/work-iq/server.json:1-27`, `microsoft/work-iq/plugins/workiq/.mcp.json:1-9`, `microsoft/work-iq/plugins/workiq/README.md:5-41` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^4]: `microsoft/work-iq/plugins/workiq/skills/workiq/SKILL.md:1-45`, `microsoft/work-iq/plugins/workiq/skills/workiq/SKILL.md:47-130`, `microsoft/work-iq/PLUGINS.md:84-123` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^5]: `microsoft/work-iq/plugins/workiq-productivity/README.md:1-39`, `microsoft/work-iq/.github/plugin/marketplace.json:31-47`, `microsoft/work-iq/plugins/workiq-productivity/.github/plugin/plugin.json:1-8` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^6]: `microsoft/work-iq/plugins/workiq-productivity/skills/action-item-extractor/SKILL.md:24-74`, `microsoft/work-iq/plugins/workiq-productivity/skills/daily-outlook-triage/SKILL.md:17-96`, `microsoft/work-iq/plugins/workiq-productivity/skills/daily-outlook-triage/SKILL.md:151-197`, `microsoft/work-iq/plugins/workiq-productivity/skills/email-analytics/SKILL.md:19-90`, `microsoft/work-iq/plugins/workiq-productivity/skills/email-analytics/SKILL.md:165-217`, `microsoft/work-iq/plugins/workiq-productivity/skills/meeting-cost-calculator/SKILL.md:21-85`, `microsoft/work-iq/plugins/workiq-productivity/skills/meeting-cost-calculator/SKILL.md:144-214`, `microsoft/work-iq/plugins/workiq-productivity/skills/org-chart/SKILL.md:18-174` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^7]: `microsoft/work-iq/plugins/workiq-productivity/skills/multi-plan-search/SKILL.md:18-109`, `microsoft/work-iq/plugins/workiq-productivity/skills/multi-plan-search/SKILL.md:168-220`, `microsoft/work-iq/plugins/workiq-productivity/skills/site-explorer/SKILL.md:19-217`, `microsoft/work-iq/plugins/workiq-productivity/skills/channel-audit/SKILL.md:20-117`, `microsoft/work-iq/plugins/workiq-productivity/skills/channel-audit/SKILL.md:183-220`, `microsoft/work-iq/plugins/workiq-productivity/skills/channel-digest/SKILL.md:19-107`, `microsoft/work-iq/plugins/workiq-productivity/skills/channel-digest/SKILL.md:167-220` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^8]: `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/README.md:1-35`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/install-atk/SKILL.md:22-63`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/declarative-agent-developer/SKILL.md:18-49`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/declarative-agent-developer/SKILL.md:83-159`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/SKILL.md:17-33`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/SKILL.md:73-120`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/SKILL.md:131-188`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/SKILL.md:192-259` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^9]: `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/declarative-agent-developer/references/scaffolding-workflow.md:1-17`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/declarative-agent-developer/references/scaffolding-workflow.md:67-108`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/declarative-agent-developer/references/scaffolding-workflow.md:125-155`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/references/copilot-widget-protocol.md:17-58`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/references/copilot-widget-protocol.md:59-106`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/references/copilot-widget-protocol.md:108-188`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/skills/ui-widget-developer/references/copilot-widget-protocol.md:208-235` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^10]: `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:7-44`, `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:61-104`, `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:108-137`, `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:141-173`, `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:177-249` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^11]: `microsoft/work-iq/scripts/Enable-WorkIQToolsForTenant.ps1:19-57`, `microsoft/work-iq/scripts/Enable-WorkIQToolsForTenant.ps1:64-167`, `microsoft/work-iq/scripts/Verify-WorkIQTenant.ps1:15-142` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^12]: `microsoft/work-iq/CONTRIBUTING.md:5-48`, `microsoft/work-iq/CONTRIBUTING.md:50-95`, `microsoft/work-iq/AGENTS.md:92-149` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
[^13]: `microsoft/work-iq/server.json:6-11`, `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:20-24`, `microsoft/work-iq/ADMIN-INSTRUCTIONS.md:294-298`, `microsoft/work-iq/.github/plugin/marketplace.json:20-29`, `microsoft/work-iq/plugins/microsoft-365-agents-toolkit/.github/plugin/plugin.json:1-8` (commit `c5e6e58d9794100c2480e8753bf5b490b389ab7d`)
