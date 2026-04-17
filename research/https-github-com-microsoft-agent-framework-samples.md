# Microsoft Agent Framework Samples (`microsoft/Agent-Framework-Samples`) — technical research report

**Repository:** [microsoft/Agent-Framework-Samples](sources/microsoft-agent-framework-samples/repo/README.md)  
**Snapshot analyzed:** `6ef0c1d590bbbe2552182060e114b8000a0e155e`  
**Research basis:** local source inspection of the mirrored repository under `sources/microsoft-agent-framework-samples/repo`.[^1]

## Executive summary

`microsoft/Agent-Framework-Samples` is best understood as a **curriculum and integration repo** for Microsoft Agent Framework, not as the framework's core implementation. It organizes a guided path from beginner tutorials to provider demos, tool demos, workflow orchestration, evaluation/observability, and finally end-to-end case studies that package the framework into fuller applications.[^1][^3]

The repo is intentionally **bi-modal by language**. The early .NET samples are runnable console or ASP.NET projects that build `AIAgent` or `Workflow` objects directly from provider clients, while the early Python content is more notebook-centric and tutorial-oriented; the richer Python implementations arrive later in `08.EvaluationAndTracing` and `09.Cases`, where the repo switches to executable Python packages, FastAPI hosts, DevUI entrypoints, and longer-lived orchestration modules.[^3][^4][^8][^9]

The repo's strongest value is as an **architecture cookbook**: it shows concrete patterns for GitHub Models, Azure OpenAI, Microsoft Foundry, Foundry Local, hosted tools, MCP, sequential/fan-out/conditional workflows, DevUI, AG-UI, OpenTelemetry, declarative YAML ingestion, and even a managed-agent style durable session harness. Its main limitation is that many samples are still workshop-style assets rather than turnkey starters: docs recommend preview/source installs, .NET projects often carry placeholder project references, and the root README explicitly calls out Windows long-path setup as an operational concern.[^2][^4][^15]

## Architecture / system overview

At a high level, the repository has two lanes:

```text
Learner / app developer
   |
   +--> 00-08 tutorial chapters
   |      |
   |      +--> provider bootstrap
   |      |      +--> GitHub Models
   |      |      +--> Azure OpenAI
   |      |      +--> Microsoft Foundry
   |      |      +--> Foundry Local
   |      |
   |      +--> agent layer
   |      |      +--> AIAgent / ChatAgent
   |      |
   |      +--> capability layer
   |      |      +--> tools (code, file search, MCP, web search)
   |      |      +--> workflows (sequential / concurrent / conditional)
   |      |
   |      +--> host / debug layer
   |             +--> DevUI
   |             +--> AG-UI
   |             +--> OpenTelemetry
   |
   +--> 09.Cases integrated apps
          |
          +--> marketing workflow
          +--> local deep research + evaluation
          +--> declarative YAML runtime
          +--> managed-agent harness
```

That split is visible in the root README and the chapter layout. `00.ForBeginners` and `01.AgentFoundation` are pedagogical on-ramps; `02` through `08` are capability-focused samples; and `09.Cases` collects larger scenarios that resemble application blueprints more than lessons.[^1][^3]

## What the repository contains

| Area | What it contains | Why it matters |
| --- | --- | --- |
| `00.ForBeginners` | Notebook-first extension of Microsoft's "AI Agents for Beginners" curriculum | Shows how the repo is meant to be consumed as a learning path rather than a library drop-in.[^3] |
| `02.CreateYourFirstAgent` + `03.ExploerAgentFramework` | Minimal provider bootstrap samples | Establish the recurring client -> chat client -> `AIAgent` pattern used throughout the repo.[^4][^5] |
| `04.Tools` + `05.Providers` + `06.RAGs` | Tooling and provider integration samples | Demonstrate hosted code execution, file search, and MCP-backed grounding over Foundry services.[^6] |
| `07.Workflow` | Core orchestration patterns | Shows how the samples move from single agents to graph-based workflows with fan-out/fan-in and conditional routing.[^7] |
| `08.EvaluationAndTracing` | Debug/observe samples | Demonstrates DevUI, AG-UI-adjacent debugging, and OpenTelemetry instrumentation in both runtimes.[^8][^9] |
| `09.Cases` | Production-shaped reference apps | Contains the most interesting samples: marketing workflows, local deep research, YAML-driven Foundry handoff, travel AG-UI apps, and a managed-agent harness.[^1][^10][^11][^12][^13][^14] |

## 1. This repo is a guided sample curriculum, not a packaged SDK

The root README presents the repository as a "hands-on guide" with a fixed chapter progression, explicit prerequisite setup, and a cross-matrix of .NET and Python examples per directory. It also describes two setup modes--install the released framework packages or build Agent Framework from source--which is a strong signal that this repo assumes readers are learning and experimenting with an evolving platform rather than consuming a sealed, versioned sample pack.[^1][^2]

That tutorial orientation is even clearer in `00.ForBeginners`. The folder explicitly says it extends the separate `microsoft/ai-agents-for-beginners` curriculum, and it describes each lesson as a Jupyter notebook with explanations, step-by-step code, and best practices. `01.AgentFoundation`, by contrast, is essentially conceptual documentation about what agents are and how Foundry / Agent Framework fit together, which reinforces that the early part of the repo is mostly educational scaffolding.[^3]

Operationally, the root repo is also opinionated about environment setup. It centralizes GitHub Models, Azure OpenAI, Foundry Local, Foundry project, Bing, and OTLP configuration in `.env`, and it explicitly calls out Windows ARM64 / `core.longpaths` setup. That is unusually practical documentation for a samples repo, and it mattered in practice here because some deep sample paths do exceed default Windows checkout limits.[^2]

## 2. The recurring .NET implementation pattern is "provider client -> chat client -> AIAgent"

The simplest .NET sample in `02.CreateYourFirstAgent` is representative. It loads `.env`, constructs an `OpenAIClient` pointed at the GitHub Models endpoint, calls `GetChatClient(...).AsIChatClient().AsAIAgent(...)`, and then runs both standard and streaming interactions. The same sample also attaches a local function tool (`GetRandomDestination`) through `AIFunctionFactory.Create(...)`, which shows the repo's preferred baseline shape: **host SDK client first, Agent Framework wrapper second, optional tools third**.[^4]

The corresponding `.csproj` is revealing too. It targets `net10.0`, mixes normal NuGet package references (`Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `OpenAI`, `DotNetEnv`) with placeholder `ProjectReference` entries for `Microsoft.Agents.AI` and `Microsoft.Agents.AI.OpenAI`, and therefore assumes you will either replace those placeholders or point at a local framework checkout. That makes these samples more like workshop templates than fully self-contained starter repos.[^4][^15]

Provider exploration in `03.ExploerAgentFramework` keeps the same shape while swapping the backing client. Azure OpenAI uses `AzureOpenAIClient(...).GetChatClient(...).AsAIAgent(...)`; GitHub Models uses `OpenAIClient` with a custom endpoint; Microsoft Foundry uses `AIProjectClient` plus `AgentAdministrationClient.CreateAgentVersionAsync(...)` and `aiProjectClient.AsAIAgent(...)`; and Foundry Local again goes through an OpenAI-compatible endpoint with a dummy API key. Across all of them, the top-level object exposed to the sample author is still an `AIAgent`.[^5]

## 3. Hosted tools, MCP, and file-backed RAG are treated as first-class sample themes

The tools samples show that the repo is not limited to plain prompt/response chat. In Foundry-backed .NET examples, hosted capabilities are added directly as tool definitions: `HostedCodeInterpreterTool` for code execution, `FileSearchTool` over a vector store built from uploaded files, and MCP tool definitions that point at `https://learn.microsoft.com/api/mcp` with explicit allow-lists and approval policies.[^6]

The MCP sample is especially notable because it demonstrates a human approval loop rather than fire-and-forget tool calling. The program creates an MCP-backed Foundry agent, inspects returned `ToolApprovalRequestContent`, prompts the user to approve or deny each tool call, wraps the approval result into a follow-up `ChatMessage`, and only then continues the run. That makes the repo valuable not just for "how to attach a tool," but for **how to operationalize human-in-the-loop tool approval** in a real agent session.[^6]

The file-search samples likewise go beyond toy prompts. The .NET RAG example uploads `demo.md` into Foundry storage, creates a vector store, binds that store to a `FileSearchTool`, and then creates a declarative agent version that is instructed to answer only from those documents. This shows the repo's preferred RAG pattern as **service-hosted retrieval + tool-bound agent**, not a custom retrieval pipeline reimplemented in sample code.[^6]

## 4. Workflow orchestration is the repo's most important advanced concept

`07.Workflow` frames the architecture in graph terms--executors, edges, and workflow orchestration--and the .NET samples make that concrete. The basic sample wires a `FrontDesk` agent into a `Concierge` reviewer with `WorkflowBuilder(...).AddEdge(...)`; the concurrent sample introduces a custom start executor plus an aggregation executor, then uses `AddFanOutEdge(...)` and `AddFanInBarrierEdge(...)` to broadcast work and rejoin results; and the conditional sample routes draft content through review and publication branches using edge predicates plus typed executors that yield workflow outputs.[^7]

The workflow samples also surface two practical engineering traits that are easy to miss in higher-level docs. First, the .NET examples treat workflows as inspectable artifacts, exporting Mermaid and DOT graphs to help you visualize the runtime graph. Second, they treat executors as real programmable nodes, not just opaque wrappers around agents--the concurrent and conditional samples both define custom executors with their own message handlers and typed output contracts.[^7]

The Python case studies reinforce the same message from a different angle. The Foundry Local deep-research sample implements explicit executor classes (`StartExecutor`, `ResearchAgentExecutor`, `IterationControlExecutor`, `FinalReportExecutor`) around a looped workflow, while the marketing case uses `SequentialBuilder` for the main pipeline and swaps in a `DeepResearchExecutor` when research mode is enabled. In other words, the repo consistently teaches that Agent Framework becomes most interesting when agents are **nodes inside orchestrated graphs**, not standalone chat wrappers.[^10][^11]

## 5. DevUI, AG-UI, and OpenTelemetry are treated as product surfaces, not debugging afterthoughts

The .NET DevUI sample is effectively an ASP.NET Core host template. It adds a chat client to DI, registers hosted agents, composes a workflow as an agent with `builder.AddWorkflow(...).AddAsAIAgent()`, enables OpenAI-style response and conversation endpoints, and conditionally maps `MapDevUI()` in development. The paired `.csproj` also references the Agent Framework hosting, workflow, and DevUI projects directly, which makes this sample a good picture of how Microsoft expects local debugging surfaces to be embedded into a web host.[^8]

The AG-UI samples follow the same pattern for frontend integration. On the Python side, the server is a minimal FastAPI app that converts a workflow into an agent via `workflow.as_agent(...)` and then exposes it with `add_agent_framework_fastapi_endpoint(app, agent, "/")`. On the .NET side, the host adds AG-UI services, maps `MapAGUI("/", ...)`, and uses `ChatClientAgentFactory` to build the same dual-agent travel workflow and expose it as a single AG-UI-compatible agent. The repo is therefore demonstrating **protocol-first hosting**, not just in-process workflow execution.[^8][^9]

Observability is similarly first-class. The Python telemetry samples call `configure_otel_providers()` and create traced workflow/agent sessions, while the .NET OpenTelemetry sample stands up explicit tracer, meter, and logging providers, instruments Agent Framework sources, wraps individual agents and the workflow agent with `.UseOpenTelemetry(...)`, and records custom counters and latency histograms around each interaction. That makes the samples unusually strong on **operational observability**, not just functionality.[^8][^9]

## 6. `09.Cases` is where the repo becomes most architecturally interesting

### 6.1 Agentic marketing workflow

`09.Cases/AgenticMarketingContentGen` is a serious multi-stage application, not a toy. Its README describes a four-stage pipeline (strategy, copywriting, image, video) with optional deep research, and the implementation backs that up: `AgenticMarketingWorkflow` builds a tool registry, conditionally enables Tavily search, FLUX image generation, Sora video generation, and deep research, then creates a sequential workflow with checkpointing and a packaging executor that writes campaign artifacts to timestamped directories.[^10]

The most interesting part is the deep-research branch. `DeepResearchExecutor` is not just a prompt alias; it creates three internal agents (planner, researcher, analyst), has the researcher call a real web search tool, and synthesizes the results back into the same `MarketingStrategy` JSON shape that downstream copy/image/video agents expect. This is a good example of using Agent Framework to hide a **nested orchestration subgraph behind one executor interface**.[^10]

### 6.2 Foundry Local deep research + evaluation

`09.Cases/FoundryLocalPipeline` shows a different but equally important pattern: using Agent Framework as the glue around local model runtime, evaluation, and workflow tooling. One script creates a `FoundryLocalClient(...).as_agent(...)` and then runs Azure AI red-team evaluation over it across multiple risk categories and attack strategies; the other builds an iterative research workflow with a `search_web` tool, loop-control executors, and DevUI integration. The repo is therefore using Agent Framework as an orchestration and evaluation layer **even when the underlying model is running locally**, not only in Azure-hosted scenarios.[^11]

### 6.3 Low-code Foundry -> YAML -> native Agent Framework runtime

`09.Cases/MicrosoftFoundryWithAITKAndMAF` is valuable because it captures a deployment path, not just a coding technique. The README lays out a three-stage flow--design in Microsoft Foundry, sync locally through the VS Code tooling, then run on Microsoft Agent Framework as configuration-as-code--and the checked-in YAML proves the point: `workflow.yaml` uses declarative actions such as `InvokeAzureAgent`, `Question`, `ConditionGroup`, `GotoAction`, and `EndConversation`, while the `CreateWorkflowWithYAML` program loads that YAML into a `WorkflowFactory` and executes it with almost no imperative orchestration code.[^12]

### 6.4 GHModel.AI travel-assistant integration set

The `GHModel.AI` case family is effectively the repo's integration lab. The shared README explains that the same dual-agent travel workflow is exposed three ways--AG-UI, DevUI, and OpenTelemetry--and the implementation shows the same conceptual graph moving across FastAPI, ASP.NET Core, and console/CLI telemetry hosts. If you want to know how Microsoft imagines a workflow becoming a debuggable, streamable, front-end-consumable application surface, this subtree is the clearest answer in the repo.[^8][^9][^14]

### 6.5 Managed-agent harness on Foundry

`09.Cases/maf_harness_managed_agent` is the repo's most opinionated systems sample. It maps Anthropic's "managed agent" architecture onto Agent Framework and Foundry: `SessionLog` implements an append-only durable event log, `AgentHarness` is an explicitly stateless orchestration brain over `FoundryChatClient`, sandbox tools are provisioned lazily on first use, and the hosting layer exposes `/sessions`, `/run`, `/events`, `/wake`, and `/health` endpoints via Azure Functions or FastAPI. This is far beyond tutorial content--it is a reference design for **recoverable, horizontally scalable, tool-using agent sessions**.[^13]

## 7. What to copy from this repo vs. what to treat as workshop scaffolding

The best reusable material in this repository is the **pattern language**: provider bootstrapping around `AIAgent` / `ChatAgent`, graph-based workflow composition, DevUI / AG-UI / OpenTelemetry host wiring, and the case-study architectures in `09.Cases`. Those sections show how the framework is used in practice and are close to what you would adapt into a real application or internal accelerator.[^5][^7][^8][^9][^10][^11][^12][^13]

By contrast, the early chapters are most useful as onboarding material. They are excellent for learning the concepts, but many of them are notebook-based, docs-heavy, or dependent on manual environment/project-path edits. If someone wanted a production starter, I would point them first at `GHModel.AI`, `AgenticMarketingContentGen`, `FoundryLocalPipeline`, or `maf_harness_managed_agent`, not at `00.ForBeginners` or `02.CreateYourFirstAgent`.[^3][^4][^10][^11][^13][^14]

There is also some version and packaging drift across the repo. The root README says .NET 9.0+, `GHModel.AI` advertises .NET 8.0+, and representative project files target `net10.0`; similarly, the root quick start mixes released-package instructions with source-build guidance and placeholder project references. I would therefore treat the repo as a **fast-moving workshop snapshot** rather than a stable compatibility contract.[^2][^4][^14][^15]

## Confidence assessment

**High confidence** on the repository's overall role, chapter layout, provider patterns, workflow patterns, and the architecture of the advanced case studies. Those conclusions are grounded directly in the root README, chapter READMEs, representative .NET sample projects, and the executable Python case-study modules rather than secondary summaries.[^1][^4][^7][^10][^11][^12][^13]

**Medium confidence** on the fine-grained behavior of the early Python examples, because much of the introductory Python material is stored as notebooks and this research deliberately prioritized runnable source modules and host applications over exhaustively reverse-engineering notebook JSON. The high-level parity claims are well supported by the curriculum READMEs, but the later case studies provide the clearest code-level evidence for Python runtime patterns.[^3][^9]

**Low uncertainty overall** on the architectural findings, but some setup guidance should be read as per-commit workshop documentation rather than long-term support policy. The repo's own docs and project files show signs of active churn--especially around target framework versions, source-build assumptions, and preview package usage--so operational details may move faster than the core sample patterns.[^2][^4][^8][^15]

## Footnotes

[^1]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/README.md:1-33,130-158,168-181`; `sources/microsoft-agent-framework-samples/repo/09.Cases/README.md:1-18`.
[^2]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/README.md:34-127`; `sources/microsoft-agent-framework-samples/repo/.env.examples:1-19`.
[^3]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/00.ForBeginners/README.md:1-18,75-123`; `sources/microsoft-agent-framework-samples/repo/01.AgentFoundation/README.md:1-40`.
[^4]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/02.CreateYourFirstAgent/code_samples/dotNET/dotnet-travelagent-ghmodel/Program.cs:1-68`; `sources/microsoft-agent-framework-samples/repo/02.CreateYourFirstAgent/code_samples/dotNET/dotnet-travelagent-ghmodel/dotnet-travelagent-ghmodel.csproj:1-23`.
[^5]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/03.ExploerAgentFramework/code_samples/dotNET/01-dotnet-agent-framework-aoai/Program.cs:1-27`; `sources/microsoft-agent-framework-samples/repo/03.ExploerAgentFramework/code_samples/dotNET/02-dotnet-agent-framework-ghmodel/Program.cs:1-42`; `sources/microsoft-agent-framework-samples/repo/03.ExploerAgentFramework/code_samples/dotNET/03-dotnet-agent-framework-msfoundry/Program.cs:1-46`; `sources/microsoft-agent-framework-samples/repo/03.ExploerAgentFramework/code_samples/dotNET/04-dotnet-agent-framework-foundrylocal/Program.cs:1-44`.
[^6]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/04.Tools/code_samples/dotNET/msfoundry/02-dotnet-agent-framework-msfoundry-code-interpreter/Program.cs:1-59`; `sources/microsoft-agent-framework-samples/repo/04.Tools/code_samples/dotNET/msfoundry/04-dotnet-agent-framework-msfoundry-file-search/Program.cs:1-59`; `sources/microsoft-agent-framework-samples/repo/05.Providers/code_samples/dotNET/01-dotnet-agent-framework-aifoundry-mcp/AgentMCP.Console/Program.cs:1-75`.
[^7]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/07.Workflow/README.md:1-37`; `sources/microsoft-agent-framework-samples/repo/07.Workflow/code_samples/dotNET/01.dotnet-agent-framework-workflow-ghmodel-basic/Program.cs:25-94`; `sources/microsoft-agent-framework-samples/repo/07.Workflow/code_samples/dotNET/03.dotnet-agent-framework-workflow-ghmodel-concurrent/Program.cs:25-140`; `sources/microsoft-agent-framework-samples/repo/07.Workflow/code_samples/dotNET/04.dotnet-agent-framework-workflow-msfoundry-condition/Program.cs:21-249`.
[^8]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/README.md:1-49`; `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/dotNET/GHModel.dotNET.AI.Workflow.DevUI/Program.cs:28-127`; `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/dotNET/GHModel.dotNET.AI.Workflow.DevUI/GHModel.dotNET.AI.Workflow.DevUI.csproj:1-25`; `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/GHModel.dotNET.AI/GHModel.dotNET.AI.Workflow.AGUI/GHModel.dotNET.AI.Workflow.AGUI.Server/Program.cs:20-89`; `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/GHModel.dotNET.AI/GHModel.dotNET.AI.Workflow.AGUI/GHModel.dotNET.AI.Workflow.AGUI.Server/ChatClientAgentFactory.cs:19-77`; `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/GHModel.dotNET.AI/GHModel.dotNET.AI.Workflow.OpenTelemetry/Program.cs:25-225`.
[^9]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/python/multi_workflow_ghmodel_devui/main.py:1-22`; `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/python/multi_workflow_ghmodel_devui/travelplan_workflow/workflow.py:1-5`; `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/GHModel.Python.AI/GHModel.Python.AI.Workflow.AGUI/GHModel.Python.AI.Workflow.AGUI.Server/main.py:1-26`; `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/GHModel.Python.AI/GHModel.Python.AI.Workflow.OpenTelemetry/main.py:16-67`; `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/python/tracer_aspire/simple.py:16-66`.
[^10]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/09.Cases/AgenticMarketingContentGen/README.md:3-16,45-110,152-231`; `sources/microsoft-agent-framework-samples/repo/09.Cases/AgenticMarketingContentGen/marketing_workflow/workflow.py:37-155,161-275`; `sources/microsoft-agent-framework-samples/repo/09.Cases/AgenticMarketingContentGen/marketing_workflow/agents.py:19-27,76-149,154-217,229-270,275-320`; `sources/microsoft-agent-framework-samples/repo/09.Cases/AgenticMarketingContentGen/marketing_workflow/research.py:24-152,155-239,240-320`.
[^11]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/09.Cases/FoundryLocalPipeline/README.md:5-16,17-45,53-85,89-145`; `sources/microsoft-agent-framework-samples/repo/09.Cases/FoundryLocalPipeline/01.foundrylocal_maf_evaluation.py:16-101`; `sources/microsoft-agent-framework-samples/repo/09.Cases/FoundryLocalPipeline/02.foundrylocal_maf_workflow_deep_research_devui.py:1-18,30-102,108-149,156-317`.
[^12]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/09.Cases/MicrosoftFoundryWithAITKAndMAF/README.md:1-45`; `sources/microsoft-agent-framework-samples/repo/09.Cases/MicrosoftFoundryWithAITKAndMAF/YAML/workflow.yaml:1-49`; `sources/microsoft-agent-framework-samples/repo/09.Cases/MicrosoftFoundryWithAITKAndMAF/YAML/hiring_manager_agent.yaml:1-18`; `sources/microsoft-agent-framework-samples/repo/09.Cases/MicrosoftFoundryWithAITKAndMAF/YAML/apply_agent.yaml:1-18`; `sources/microsoft-agent-framework-samples/repo/09.Cases/MicrosoftFoundryWithAITKAndMAF/CreateWorkflowWithYAML/Program.cs:14-22`.
[^13]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/09.Cases/maf_harness_managed_agent/README.md:13-72,76-140,259-300`; `sources/microsoft-agent-framework-samples/repo/09.Cases/maf_harness_managed_agent/main.py:1-18,39-57,80-208`; `sources/microsoft-agent-framework-samples/repo/09.Cases/maf_harness_managed_agent/maf_harness/harness/harness.py:4-20,32-80,83-99,102-186,191-320`; `sources/microsoft-agent-framework-samples/repo/09.Cases/maf_harness_managed_agent/maf_harness/session/session_log.py:1-18,29-175`; `sources/microsoft-agent-framework-samples/repo/09.Cases/maf_harness_managed_agent/maf_harness/hosting/azure_function_host.py:1-29,50-127,129-235`.
[^14]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/README.md:12-20,24-68,98-125,215-235`.
[^15]: Snapshot `6ef0c1d590bbbe2552182060e114b8000a0e155e`. `sources/microsoft-agent-framework-samples/repo/README.md:72-88`; `sources/microsoft-agent-framework-samples/repo/09.Cases/GHModel.AI/README.md:3-5,73-77`; `sources/microsoft-agent-framework-samples/repo/02.CreateYourFirstAgent/code_samples/dotNET/dotnet-travelagent-ghmodel/dotnet-travelagent-ghmodel.csproj:3-20`; `sources/microsoft-agent-framework-samples/repo/08.EvaluationAndTracing/dotNET/GHModel.dotNET.AI.Workflow.DevUI/GHModel.dotNET.AI.Workflow.DevUI.csproj:1-25`.
