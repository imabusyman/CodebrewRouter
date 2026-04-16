# Microsoft Agent Framework (`microsoft/agent-framework`) — technical research report

## Executive Summary

Microsoft Agent Framework is a source-available, multi-language agent platform focused on **Python and .NET**, with first-class support for chat agents, graph-based workflows, tool/function calling, observability, and multiple hosting/protocol surfaces such as A2A, AG-UI, DevUI, declarative YAML, and durable execution.[^1][^2][^3]

The repo is not a single monolith: it is organized as a **Python workspace of many packages** and a **.NET solution of many assemblies**, with the core runtime split from provider/integration packages so developers can compose just the pieces they need.[^3][^4][^5]

Architecturally, both runtimes converge on the same model: an **agent abstraction** wraps a lower-level chat client, sessions/context providers supply conversation state and injected context, tool layers expose functions and sub-agents, and workflow runtimes turn agents and deterministic executors into resumable graphs.[^6][^7][^8][^9]

The most distinctive parts of the codebase are its **workflow engine** (Pregel-style supersteps in Python and executor/edge graphs in .NET), its emphasis on **provider-leading client packages** rather than a bloated core, and its deliberate investment in **developer/runtime infrastructure**: OpenTelemetry, durable hosting, protocol adapters, and declarative authoring.[^10][^11][^12][^13]

## Architecture / System Overview

At the highest level, the repository looks like this:

```text
Application code
   |
   +--> Agent
   |      |
   |      +--> Session / context providers / history
   |      +--> Tool + function invocation layers
   |      +--> Middleware + telemetry
   |      +--> Provider chat client (OpenAI, Foundry, Anthropic, etc.)
   |
   +--> Workflow
          |
          +--> Executors / edges / routers / sub-workflows
          +--> Checkpointing / HITL / streaming / durable execution
          +--> Optional agent host wrapper
```

That shape is visible in both implementations. The root README describes the framework as a platform for building, orchestrating, and deploying agents, highlights graph workflows, DevUI, observability, multi-provider support, and middleware, and points directly to both the Python and .NET implementations; representative runtime packages in each language then show the split between core abstractions, workflows, providers, hosting, and protocol adapters.[^1][^3]

The **.NET side** is spread across assemblies such as `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Abstractions`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Agents.AI.Foundry`, `Microsoft.Agents.AI.DurableTask`, `Microsoft.Agents.AI.Hosting`, `Microsoft.Agents.AI.A2A`, `Microsoft.Agents.AI.Declarative`, and others.[^3] The **Python side** is a workspace containing `core`, `openai`, `foundry`, `declarative`, `devui`, `durabletask`, `a2a`, `ag-ui`, `orchestrations`, and additional provider packages such as `anthropic`, `bedrock`, `ollama`, and `github_copilot`.[^5]

The repo is also opinionated about packaging maturity. The umbrella Python package `agent-framework` is stable and simply depends on `agent-framework-core[all]==1.0.1`, while `agent-framework-core`, `agent-framework-openai`, and `agent-framework-foundry` are marked `released`; many optional integrations remain `beta`, and at least one (`agent-framework-gemini`) is still `alpha`.[^4]

## Key Repositories / Subtrees Summary

| Subtree | Purpose | Evidence |
|---|---|---|
| [`microsoft/agent-framework`](sources/microsoft-agent-framework/repo/README.md) root | Top-level product docs, architecture entry point, samples, ADRs | README and linked language surfaces[^1][^3] |
| `dotnet/src/Microsoft.Agents.AI*` | .NET agent abstractions, workflows, hosting, providers, durable extensions | Representative runtime packages and extensions[^3] |
| `python/packages/core/agent_framework` | Python agent/session/tool/workflow runtime | Workspace membership plus core source files[^5][^7][^8][^10] |
| `python/packages/foundry` + `dotnet/src/Microsoft.Agents.AI.Foundry` | Foundry-specific clients and agent wrappers | Foundry client/agent code[^12][^14] |
| `python/packages/declarative` + `declarative-agents/` | YAML-driven declarative workflows and sample definitions | Declarative package exports and sample docs[^15] |
| `python/packages/devui`, `python/packages/a2a`, `python/packages/ag-ui`, `dotnet/src/Microsoft.Agents.AI.A2A`, `dotnet/src/Microsoft.Agents.AI.DurableTask` | Protocol adapters, local developer UI, durable/runtime hosting | Protocol/hosting sources and sample docs[^16][^17] |

## Core Runtime Model

### 1. Agents are the top-level abstraction

In .NET, `AIAgent` is the core abstraction for agent interactions and conversation management. Its own class-level remarks explicitly frame agents as orchestrators across trust boundaries, note that one agent instance may participate in multiple concurrent conversations, and make session creation/serialization/deserialization abstract responsibilities of concrete implementations.[^6]

In Python, the public-facing `Agent` stack is layered on top of `BaseAgent`/`RawAgent`. `BaseAgent` holds identity, context providers, middleware, and session helpers, while `RawAgent` binds an agent to a `SupportsChatGetResponse` chat client, default instructions, default options, tools, MCP tools, compaction, and tokenizer state.[^7]

### 2. Sessions are first-class, not an afterthought

On .NET, `AgentSession` is explicitly the durable state container for a conversation: it can hold history, memory references, and arbitrary serialized state, and it is intentionally created by the agent so the agent can attach required behaviors.[^6]

On Python, `AgentSession` and `SessionContext` separate **persisted state** from **per-invocation working context**. `SessionContext` carries input messages, provider-added context messages, instructions, tools, middleware, options, and metadata for a single run, while the session itself stores durable state that providers can serialize/deserialize through the session state dictionary.[^8]

The Python runtime also has a pragmatic history behavior: if a caller passes a session but the agent has no configured context providers, no service session id, and no service-side storage indicators, it auto-injects an `InMemoryHistoryProvider` so a session still behaves conversationally.[^9]

### 3. Tools and sub-agents are treated as composable capabilities

In .NET, `AIAgentExtensions.AsAIFunction()` exposes an agent as an `AIFunction`, carrying forward additional properties from an enclosing `FunctionInvokingChatClient` context and warning that shared sessions should not be used concurrently in unpredictable ways.[^18]

In Python, `BaseAgent.as_tool()` turns an agent into a `FunctionTool`, supports optional shared-session propagation, streams the delegated agent run under the hood, and raises `UserInputRequiredException` when the delegated agent returns approval or other user-input requests instead of a final text response.[^18]

This “agent-as-tool” capability is one of the framework’s most important architectural moves because it lets higher-level agents and workflows reuse lower-level agents without inventing a second orchestration model.[^18]

## How request execution actually works

### .NET request path

The .NET chat-agent composition is explicit in `ChatClientExtensions`. `IChatClient.AsAIAgent()` wraps a chat client in a `ChatClientAgent`, and `WithDefaultAgentMiddleware()` ensures there is a `FunctionInvokingChatClient`; if per-service-call history persistence is requested, it inserts a `PerServiceCallChatHistoryPersistingChatClient` between the function-invoking layer and the leaf client.[^19]

Separately, `FunctionInvocationDelegatingAgentBuilderExtensions.Use(...)` adds function-invocation callbacks at the agent level, but only if the wrapped agent already exposes a `FunctionInvokingChatClient`; otherwise it throws, which makes the agent pipeline’s dependency on chat-level function invocation explicit rather than implicit.[^19]

### Python request path

Python’s run path is easier to trace because most of it lives in one file. `RawAgent.run()` normalizes messages, prepares a `_RunContext`, calls the downstream chat client, and then converts the chat response or chat stream into `AgentResponse` / `AgentResponseUpdate` objects.[^9]

The `_prepare_run_context()` method is the heart of that pipeline: it resolves sessions, optional per-service-call history persistence, provider-added instructions/tools/middleware, runtime tool lists, local MCP tool connections, service conversation ids, and final merged chat options before delegating to the chat client.[^9]

The result is a layered but mechanically simple pipeline: **context providers run first, chat options are merged, tools are normalized, middleware is accumulated, the chat client executes, then after-run providers finalize the response and persist state**.[^8][^9]

## Workflow engine

### .NET workflows

The .NET workflow runtime is executor/edge based. `Workflow` owns executor bindings, edges, ports, output executors, and a start executor id; `WorkflowBuilder` builds those graphs incrementally by binding executors and adding direct, conditional, and fan-out edges; and `Executor` is the component that configures routing protocol, handles messages, emits events, and can auto-send or auto-yield handler results.[^10]

`Workflow` also actively manages ownership and reuse. The implementation guards against reusing a running workflow, using a workflow as a subworkflow of multiple parents, or reusing shared executors that cannot be reset, which tells you the .NET runtime treats workflow instances as stateful resources rather than immutable plans.[^10]

The framework can expose a workflow back as an agent through `WorkflowHostingExtensions.AsAIAgent(...)`, which wraps a workflow in a `WorkflowHostAgent` and lets hosted workflows participate in the same agent APIs as normal chat-backed agents.[^10]

### Python workflows

Python’s `Workflow` describes itself as a **graph-based execution engine** that orchestrates connected executors in a Pregel-like model. It explicitly documents synchronized supersteps, runtime-discovered input/output types, checkpointing, human-in-the-loop continuation, and nested workflow composition through `WorkflowExecutor`.[^11]

The runtime implementation in `Runner` confirms that description. It runs iterations until convergence, emits `superstep_started` / `superstep_completed` events, drains messages in batches, delivers them concurrently across edge runners while preserving per-edge ordering, commits state at superstep boundaries, and creates checkpoints after each superstep when checkpointing is enabled.[^11]

Checkpointing is a real part of the design rather than a sample-only feature. `WorkflowCheckpoint` stores workflow name, graph signature hash, previous checkpoint link, messages, committed state, pending request-info events, and iteration count, while the runner validates graph compatibility before restoring a checkpointed run.[^11]

Python also provides `WorkflowAgent`, which wraps a workflow as an agent. It validates that the workflow’s start executor accepts `list[Message]`, turns workflow output events into agent responses, and exposes checkpoint-aware `run()` overloads so callers can resume workflow-backed agents from saved state.[^11]

### What workflows are for in practice

The sample trees show that workflows are not limited to “agent chains.” The Python workflow samples cover control flow, parallelism, state sharing, checkpoint/resume, human-in-the-loop, workflow-as-agent, declarative workflows, and orchestration patterns such as sequential, concurrent, handoff, group chat, and magentic styles; the .NET workflow samples similarly cover agents-in-workflows, sub-workflows, fan-out/fan-in, loops, shared state, and conditional routing.[^20]

## Provider and integration strategy

### Provider-leading packaging is a deliberate design choice

The Python ADR `0021-provider-leading-clients.md` says the core package should only contain abstractions, middleware infrastructure, and telemetry, while provider-specific clients move into provider-specific packages. That ADR also renames the OpenAI client surface to provider-leading names and establishes `FoundryChatClient` plus `FoundryAgent` as the preferred Azure/Foundry abstractions.[^13]

This is reflected directly in the code. `RawFoundryChatClient` is a low-level client that builds an OpenAI-compatible client from an `AIProjectClient`, and `FoundryChatClient` layers `FunctionInvocationLayer`, `ChatMiddlewareLayer`, and `ChatTelemetryLayer` on top of that raw client.[^12]

On .NET, the same pattern appears through extension-based conversions from provider SDKs into agents. `IChatClient.AsAIAgent(...)` is the generic path, while `AIProjectClient.AsAIAgent(...)` supports both server-side Foundry agents and non-versioned Responses-backed agents; `FoundryAgent` then wraps those paths and adds conveniences such as creating a Foundry conversation session that appears in the Foundry project UI.[^12][^14]

### Tool support is intentionally hybrid

The ADR on agent tools rejects a one-size-fits-all tool abstraction. Instead, it chooses a hybrid model: common tools can use generic abstractions, provider packages can expose provider-specific tool types, and `ChatOptions.RawRepresentationFactory` remains the break-glass path for unsupported cases.[^21]

That shows up concretely in Python’s `FunctionTool` and Foundry helpers. `FunctionTool` wraps Python callables, infers or accepts JSON-schema-compatible input models, tracks invocation counts/errors, and integrates with observability; `FoundryChatClient` adds helper factories for code interpreter, file search, web search, image generation, and hosted MCP tools that map directly to Foundry SDK types.[^12][^21]

## Protocols, hosting, and developer tooling

### A2A and AG-UI

Agent Framework is not limited to model-provider APIs. On .NET, `A2AClientExtensions.AsAIAgent()` converts an `A2AClient` into an `AIAgent`; on Python, the `agent_framework_a2a` package exports `A2AAgent`, and the hosting samples show how to run an agent as an A2A-compliant server, consume it from a client, or expose its skills as function tools to another agent.[^16]

The Python AG-UI package exports `AGUIChatClient`, endpoint helpers, event converters, HTTP service types, and workflow wrappers, making AG-UI another first-class integration surface rather than an afterthought.[^16]

### DevUI

DevUI is an opinionated local developer surface. The top-level README advertises it as an interactive UI for development, testing, and debugging, and the Python package implements `serve(...)` with entity registration, optional UI, optional auth, optional instrumentation, and explicit warnings when the UI is exposed on a non-localhost interface without authentication.[^1][^16]

### Declarative YAML

The repo also supports declarative authoring. The `declarative-agents/` subtree contains YAML samples for agents and workflows, the .NET declarative README shows `DeclarativeWorkflowBuilder.Build("Marketing.yaml", options)`, and the Python declarative package exports a `DeclarativeWorkflowBuilder`, `WorkflowFactory`, and action executors for agents, tools, control flow, loops, joins, and external input.[^15]

This is not a separate runtime: the Python declarative package explicitly says its YAML definitions compile into executable `Workflow` objects and inherit checkpointing, visualization, pause/resume, and runtime integration from the normal workflow engine.[^15]

### Durable execution

Durability is a major outer-ring feature. The .NET `Microsoft.Agents.AI.DurableTask` package is positioned as a Durable Task extension for stateful/durable agent execution, long-running orchestrations, automatic history management, and dashboards, using Durable Entities, Durable Orchestrations, and the Durable Task Scheduler underneath.[^17]

The Python durable package exposes durable agent client/worker/orchestration types, while the durable-task samples describe a worker-client architecture, persistent conversation state, resumable streaming, orchestration chaining, concurrent multi-agent orchestrations, and human-in-the-loop flows backed by the Durable Task Scheduler.[^17]

## Observability and safety posture

Observability is not bolted on. The root README highlights OpenTelemetry support, the .NET ADR on instrumentation chooses a wrapper pattern (`OpenTelemetryAgent`) so telemetry remains optional, and the actual implementation delegates most telemetry work to `OpenTelemetryChatClient` while retagging spans as `invoke_agent` operations and adding agent-specific attributes.[^1][^22]

Python takes a similar layered approach. `AgentTelemetryLayer` wraps agent execution with traces and token/duration histograms, while `configure_otel_providers()` and `enable_instrumentation()` separate “turn instrumentation on” from “configure providers/exporters,” which makes it easier to integrate with custom OpenTelemetry or Azure Monitor setups.[^12][^22]

The repo is also explicit that **security is the application developer’s job**. `AIAgent` warns that Agent Framework passes messages across trust boundaries without sanitization and that LLM outputs must be validated before rendering or executing; `AgentSession` warns that serialized session data may contain sensitive data and should be treated as untrusted on restore; the root README warns about third-party systems and responsible-AI obligations; and DevUI refuses insecure token generation in production-like scenarios while warning loudly on unauthenticated network exposure.[^6][^16][^23]

## How the project is meant to be consumed

For basic consumption, the root README recommends `pip install agent-framework` on Python and `dotnet add package Microsoft.Agents.AI` on .NET, then points developers to Learn docs, tutorials, migration guides, and language-specific sample trees.[^1]

For advanced Python usage, the packaging model encourages selective installs such as `agent-framework-core`, `agent-framework-foundry`, or preview connectors, which keeps dependency sets lighter when you do not need the full umbrella package.[^2][^4][^13]

For pre-release consumption, the FAQ documents a nightly-build process through GitHub Packages and NuGet package-source configuration, which is a useful signal that the project expects users to test near-tip builds as the ecosystem evolves.[^24]

## Strengths, trade-offs, and likely fit

### Strengths

The strongest technical qualities are: a consistent **agent + workflow + tool** mental model across two languages; a workflow engine that goes beyond simple chaining into checkpointed graph execution; a serious investment in hosting/protocol surfaces; and a modular packaging strategy that prevents the “core” from becoming provider-specific glue.[^10][^11][^13][^17]

### Trade-offs

The main trade-offs are ecosystem complexity and maturity variance. The Python workspace contains many packages at different stability stages, several .NET packages wrap preview or experimental dependencies, and many advanced capabilities live behind optional packages and hosting setups, so adopters should expect real architecture choices rather than a single happy-path SDK.[^4][^5][^17]

### Best fit

This repo is a good fit if you want one framework that can cover **simple agents today** and **stateful, observable, resumable, multi-agent workflows later** without forcing a platform rewrite. It is less ideal if you want a tiny, provider-specific SDK with minimal concepts, because Agent Framework deliberately exposes sessions, context providers, tools, workflows, telemetry, and hosting as explicit building blocks.[^1][^8][^10][^11]

## Confidence Assessment

**High confidence** on the repo’s primary architecture, package layout, runtime model, workflow semantics, and hosting surfaces. Those conclusions are based on the README, package manifests, ADRs, core source files, and sample indexes rather than secondary commentary.[^1][^3][^4][^10][^11]

**Medium confidence** on the exact relative maturity of the .NET subpackages, because the Python workspace has an explicit status matrix while the .NET side signals maturity more through package composition, experimental attributes, and samples than through one consolidated lifecycle table.[^4][^12][^14][^17]

**Low uncertainty overall** on the high-level findings, but any claim about rapidly changing integrations or preview packages should be interpreted as a snapshot of the repository state at the commit cited in the footnotes, not a long-term support guarantee.[^4][^12][^24]

## Footnotes

[^1]: [`README.md:10-10`, `README.md:24-80`, `README.md:159-176`](sources/microsoft-agent-framework/repo/README.md) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^2]: [`python/README.md:5-37`, `python/README.md:44-83`](sources/microsoft-agent-framework/repo/python/README.md) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^3]: [`README.md:73-80`, `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs:13-39`, `dotnet/src/Microsoft.Agents.AI.Workflows/Workflow.cs:16-38`, `dotnet/src/Microsoft.Agents.AI.Foundry/FoundryAgent.cs:18-37`, `dotnet/src/Microsoft.Agents.AI.Hosting/HostedWorkflowBuilderExtensions.cs:8-36`, `dotnet/src/Microsoft.Agents.AI.A2A/Extensions/A2AClientExtensions.cs:9-40`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^4]: [`python/pyproject.toml:1-27`, `python/PACKAGE_STATUS.md:13-42`](sources/microsoft-agent-framework/repo/python) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^5]: [`python/pyproject.toml:62-91`, `python/packages/a2a/agent_framework_a2a/__init__.py:3-16`, `python/packages/ag-ui/agent_framework_ag_ui/__init__.py:3-39`, `python/packages/declarative/agent_framework_declarative/_workflows/__init__.py:3-83`, `python/packages/durabletask/agent_framework_durabletask/__init__.py:1-108`, `python/packages/foundry/agent_framework_foundry/_chat_client.py:464-575`](sources/microsoft-agent-framework/repo/python) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^6]: [`dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs:16-36`, `dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs:138-235`, `dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs:11-57`, `dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs:76-119`](sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Abstractions) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^7]: [`python/packages/core/agent_framework/_agents.py:314-445`, `python/packages/core/agent_framework/_agents.py:578-766`](sources/microsoft-agent-framework/repo/python/packages/core/agent_framework/_agents.py) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^8]: [`python/packages/core/agent_framework/_sessions.py:154-320`, `python/packages/core/agent_framework/_agents.py:1372-1450`](sources/microsoft-agent-framework/repo/python/packages/core/agent_framework) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^9]: [`python/packages/core/agent_framework/_agents.py:787-815`, `python/packages/core/agent_framework/_agents.py:844-1012`, `python/packages/core/agent_framework/_agents.py:1150-1450`](sources/microsoft-agent-framework/repo/python/packages/core/agent_framework/_agents.py) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^10]: [`dotnet/src/Microsoft.Agents.AI.Workflows/Workflow.cs:16-104`, `dotnet/src/Microsoft.Agents.AI.Workflows/Workflow.cs:133-230`, `dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs:15-38`, `dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs:99-145`, `dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs:178-340`, `dotnet/src/Microsoft.Agents.AI.Workflows/Executor.cs:160-217`, `dotnet/src/Microsoft.Agents.AI.Workflows/Executor.cs:242-310`, `dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowHostingExtensions.cs:8-48`](sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^11]: [`python/packages/core/agent_framework/_workflows/_workflow.py:102-171`, `python/packages/core/agent_framework/_workflows/_workflow.py:201-239`, `python/packages/core/agent_framework/_workflows/_runner.py:30-154`, `python/packages/core/agent_framework/_workflows/_runner.py:160-295`, `python/packages/core/agent_framework/_workflows/_checkpoint.py:30-88`, `python/packages/core/agent_framework/_workflows/_checkpoint.py:119-260`, `python/packages/core/agent_framework/_workflows/_agent.py:51-132`, `python/packages/core/agent_framework/_workflows/_agent.py:168-208`](sources/microsoft-agent-framework/repo/python/packages/core/agent_framework/_workflows) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^12]: [`python/packages/foundry/agent_framework_foundry/_chat_client.py:108-124`, `python/packages/foundry/agent_framework_foundry/_chat_client.py:221-275`, `python/packages/foundry/agent_framework_foundry/_chat_client.py:276-459`, `python/packages/foundry/agent_framework_foundry/_chat_client.py:464-575`, `dotnet/src/Microsoft.Agents.AI.Foundry/AzureAIProjectChatClientExtensions.cs:28-66`, `dotnet/src/Microsoft.Agents.AI.Foundry/AzureAIProjectChatClientExtensions.cs:135-247`, `dotnet/src/Microsoft.Agents.AI.Foundry/FoundryAgent.cs:18-37`, `dotnet/src/Microsoft.Agents.AI.Foundry/FoundryAgent.cs:112-147`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^13]: [`docs/decisions/0021-provider-leading-clients.md:12-42`, `docs/decisions/0021-provider-leading-clients.md:43-73`](sources/microsoft-agent-framework/repo/docs/decisions/0021-provider-leading-clients.md) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^14]: [`dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs:13-39`, `dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs:52-89`](sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^15]: [`declarative-agents/workflow-samples/README.md:1-17`, `declarative-agents/agent-samples/README.md:1-4`, `python/packages/declarative/agent_framework_declarative/_workflows/__init__.py:3-13`, `python/packages/declarative/agent_framework_declarative/_workflows/__init__.py:15-83`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^16]: [`dotnet/src/Microsoft.Agents.AI.A2A/Extensions/A2AClientExtensions.cs:9-40`, `python/packages/a2a/agent_framework_a2a/__init__.py:3-16`, `python/packages/ag-ui/agent_framework_ag_ui/__init__.py:3-39`, `python/packages/devui/agent_framework_devui/__init__.py:88-133`, `python/packages/devui/agent_framework_devui/__init__.py:135-181`, `python/packages/devui/agent_framework_devui/__init__.py:182-243`, `python/samples/04-hosting/a2a/README.md:1-9`, `python/samples/04-hosting/a2a/README.md:44-74`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^17]: [`dotnet/src/Microsoft.Agents.AI.DurableTask/README.md:1-17`, `python/packages/durabletask/agent_framework_durabletask/__init__.py:1-108`, `python/samples/04-hosting/durabletask/README.md:1-17`, `python/samples/04-hosting/durabletask/README.md:58-133`, `dotnet/samples/04-hosting/DurableWorkflows/README.md:1-18`, `dotnet/samples/04-hosting/DurableWorkflows/README.md:30-52`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^18]: [`dotnet/src/Microsoft.Agents.AI/AgentExtensions.cs:37-90`, `python/packages/core/agent_framework/_agents.py:478-572`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^19]: [`dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs:52-89`, `dotnet/src/Microsoft.Agents.AI/FunctionInvocationDelegatingAgentBuilderExtensions.cs:16-50`](sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^20]: [`python/samples/03-workflows/README.md:17-183`, `dotnet/samples/03-workflows/README.md:7-59`, `dotnet/samples/02-agents/README.md:7-21`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^21]: [`docs/decisions/0002-agent-tools.md:13-23`, `docs/decisions/0002-agent-tools.md:75-110`, `python/packages/core/agent_framework/_tools.py:210-340`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^22]: [`docs/decisions/0003-agent-opentelemetry-instrumentation.md:11-24`, `docs/decisions/0003-agent-opentelemetry-instrumentation.md:58-87`, `dotnet/src/Microsoft.Agents.AI/OpenTelemetryAgent.cs:13-30`, `dotnet/src/Microsoft.Agents.AI/OpenTelemetryAgent.cs:59-146`, `python/packages/core/agent_framework/observability.py:953-1110`, `python/packages/core/agent_framework/observability.py:1491-1627`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^23]: [`README.md:208-215`, `dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs:24-34`, `dotnet/src/Microsoft.Agents.AI.Abstractions/AgentSession.cs:45-53`, `python/packages/devui/agent_framework_devui/__init__.py:129-159`](sources/microsoft-agent-framework/repo/) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
[^24]: [`docs/FAQS.md:3-54`](sources/microsoft-agent-framework/repo/docs/FAQS.md) (commit `f112150cfbc4d514b21b60a81bbe5239b4b2c81f`)
