# Microsoft Agent Framework (`microsoft/agent-framework`) — technical research report

**Repository:** [microsoft/agent-framework](sources/microsoft-agent-framework/repo/README.md)  
**Snapshot analyzed:** `b03cb324d5cc5e91a55b5eb9045b8ead244aaf11`  
**Research basis:** local source inspection of the mirrored repository under `sources/microsoft-agent-framework/repo`.[^1]

## Executive summary

`microsoft/agent-framework` is not a thin SDK wrapper around one model API. It is a broad, multi-language agent platform with parallel .NET and Python implementations, graph/workflow runtimes, multiple hosting surfaces, OpenTelemetry instrumentation, durable execution on Durable Task, and an increasingly large set of provider integrations and experimental packages.[^2]

The repo is organized more like a product line than a single library. At the top level, Microsoft positions it as a framework for building, orchestrating, and deploying agents in both Python and .NET, with first-class emphasis on graph-based workflows, observability, middleware, DevUI, and multiple provider backends.[^2] The Python workspace reinforces that story by shipping a meta-package (`agent-framework==1.0.1`) that depends on `agent-framework-core[all]==1.0.1` and a long list of workspace subpackages, while separately tracking lifecycle state across stable, beta, and alpha packages.[^3]

The architectural center of gravity is different in each language, but the design intent is similar:

1. **.NET** centers on `AIAgent` plus `ChatClientAgent`, with agent functionality composed by decorators (`AIAgentBuilder`, telemetry wrappers, function-invocation wrappers, hosting adapters, workflow hosts).[^4]
2. **Python** centers on `RawAgent`/`Agent`, where run preparation merges sessions, tools, context providers, middleware, compaction, and provider-specific chat options before delegating to a layered chat client.[^5]
3. **Durability** is a first-class cross-language feature, not an add-on sample: both stacks map each durable agent session to a durable entity/virtual actor keyed by a shared `dafx-` naming convention and persisted conversation history model.[^6]

My overall assessment: this is a serious framework with unusually broad surface area and strong architectural ambition. Its biggest strengths are composability, hosting breadth, and cross-language alignment; its biggest costs are complexity, uneven maturity across packages, and the fact that several advanced features (especially CodeAct and some provider/integration surfaces) are still explicitly beta or alpha.[^3]

## What the repository contains

At a high level, the repo breaks into these major surfaces:

| Area | What it contains | Evidence |
| --- | --- | --- |
| Core agent SDKs | Parallel Python and .NET agent runtimes | Root README, `dotnet/src`, `python/packages`[^2] |
| Workflow/orchestration runtime | Graph workflows in .NET; orchestration patterns in Python | Root README, `.NET Workflows`, Python orchestration packages[^2][^7] |
| Hosting/protocol adapters | OpenAI-compatible hosting, A2A, Azure Functions, MCP exposure | Root README, hosting packages and sources[^2][^8] |
| Durable agents | Durable Task entity-backed sessions, orchestration helpers, Azure Functions hosting | Durable agents docs and runtime packages[^6] |
| Provider integrations | OpenAI, Foundry, Anthropic, Bedrock, Gemini, GitHub Copilot, Ollama, etc. | Python workspace sources + package status + .NET provider packages[^3][^9] |
| Experimental/advanced features | DevUI, Labs, CodeAct/Hyperlight, declarative agents | Root README, Python package status, CodeAct docs[^2][^3][^10] |

One important practical takeaway: if someone says "Agent Framework," they may mean very different things depending on language and use case. In Python, "Agent Framework" often means a workspace of packages with explicit maturity levels; in .NET, it looks more like a family of NuGet packages arranged around `Microsoft.Agents.AI`, workflows, hosting, and provider-specific adapters.[^3][^8]

## Core runtime model

### .NET: `AIAgent` as the base abstraction

In .NET, `AIAgent` is the foundational abstraction for session creation, session serialization, non-streaming runs, and streaming runs. It stores `CurrentRunContext` in an `AsyncLocal<AgentRunContext?>`, so nested tools, middleware, and child agents can access the current run context ambiently across async calls.[^4] That ambient-context choice is deliberate and is also documented in ADR `0015`, which explicitly selects a hybrid model: explicit parameters for `RunCoreAsync`, plus `AsyncLocal` for deep access by tools and middleware.[^11]

The core invocation path is straightforward: public `RunAsync(...)` overloads normalize inputs to `IEnumerable<ChatMessage>`, set `CurrentRunContext`, then delegate to `RunCoreAsync`. The matching streaming overloads do the same for `RunCoreStreamingAsync`.[^4] This gives the framework a stable, provider-agnostic top-level contract while allowing specialized agent implementations underneath.

### .NET: `ChatClientAgent` is the main concrete local-agent implementation

For ordinary model-backed agents, the main .NET implementation is `ChatClientAgent`, which adapts an `IChatClient` into an `AIAgent`. Its constructor merges instructions and tools into default `ChatOptions`, can auto-wrap the underlying chat client with default middleware, carries an optional chat history provider plus AI context providers, and warns explicitly about security boundaries around tools, prompt injection, and model output.[^12]

During execution, `ChatClientAgent` prepares session/messages, applies run-time chat-client transformations, invokes `GetResponseAsync` on the underlying `IChatClient`, updates the session's conversation identifier when needed, stamps author names, and persists/propagates history via providers.[^12] The companion `ChatClientAgentRunOptions` makes run-time overrides explicit by allowing per-run `ChatOptions` and a `ChatClientFactory` that can replace or decorate the chat client for just one invocation.[^13]

The default middleware pipeline for a .NET chat-backed agent is also opinionated. `ChatClientExtensions.WithDefaultAgentMiddleware(...)` injects `FunctionInvokingChatClient` if one is not already present, and conditionally inserts `PerServiceCallChatHistoryPersistingChatClient` when per-service-call persistence is required. The comments are explicit that ordering matters: function invocation sits outermost, with per-call history persistence between it and the leaf client.[^14]

### .NET: decorators are the main composition mechanism

The .NET stack is built around wrappers/decorators. `AIAgentBuilder` stores wrapper factories and applies them in reverse order so the first added factory becomes the outermost wrapper, matching human expectations for middleware ordering.[^15] This pattern is used for anonymous agent decorators, AI context providers, telemetry, and function-invocation middleware.

Two examples matter most:

1. **Function invocation**: `FunctionInvocationDelegatingAgent` rewrites `AgentRunOptions` into `ChatClientAgentRunOptions`, then replaces each `AIFunction` in `ChatOptions.Tools` with a `MiddlewareEnabledFunction` that routes through a `FunctionInvocationContext` and the configured middleware delegate.[^16]
2. **Telemetry**: `OpenTelemetryAgent` is a delegating wrapper that intentionally reuses `OpenTelemetryChatClient` rather than reimplementing semantic conventions itself; it forwards agent execution through a small `ForwardingChatClient`, then augments the resulting activity with agent-specific tags such as agent ID, name, description, and provider name.[^17]

This decorator-heavy design is one of the strongest architectural themes in the .NET implementation.

### Python: `RawAgent`/`Agent` as layered agent surfaces

The Python runtime uses an analogous but more explicitly layered model. `RawAgent` is the core chat-agent implementation without middleware or telemetry; `Agent` subclasses it by mixing in middleware and telemetry layers and is explicitly documented as the recommended surface for most use cases.[^5]

`RawAgent` owns the important configuration merge points: client, instructions, tools, context providers, middleware, compaction, tokenizer, and provider-specific default options. It also splits MCP tools from normal tools, storing MCP servers separately so their functions can be connected and appended at run time.[^5] This is one of the repo's recurring patterns: the public "simple" API is intentionally thin, but the actual runtime merges multiple layered sources of context and capabilities before every model call.

Execution in Python flows through `run(...)`, which first prepares a `_RunContext`, then either calls `_call_chat_client(..., stream=False)` and finalizes an `AgentResponse`, or returns a `ResponseStream` that finalizes into an `AgentResponse` once streaming completes.[^5] `_prepare_run_context(...)` is the key method: it normalizes messages, decides whether service-side storage is active, may auto-inject an in-memory history provider, resolves per-service-call history persistence, runs provider `before_run` hooks, merges provider-added tools and instructions, connects MCP tools, merges function invocation arguments, and constructs the final chat options plus client kwargs.[^5]

That makes Python's runtime feel more "session/context pipeline first" than ".NET's agent wrapper first," even though the conceptual outcome is similar.

### Python tools and MCP exposure

Python's `FunctionTool` is the core function-calling abstraction. It wraps Python callables, generates schemas from signatures or explicit models, supports declaration-only tools, carries approval metadata, and optionally constrains invocation counts and exception counts over the lifetime of the tool instance.[^18]

Python agents also have a built-in MCP server export path: `RawAgent.as_mcp_server(...)` builds an MCP server that exposes the agent as a single tool, lists tool metadata from the agent-derived schema, invokes the wrapped agent tool on `call_tool`, and currently forwards text content while warning that rich content forwarding through this MCP path is not yet implemented.[^19]

## Provider model and client layering

### Python: provider-leading clients are a conscious design choice

ADR `0021` is important for understanding the Python direction. It explicitly argues that core should contain abstractions, middleware, and telemetry only; provider-specific clients should move out of core; import paths should be provider-leading for discoverability; and Foundry should have a first-class `FoundryAgent` abstraction instead of forcing every remote-agent scenario through generic client composition.[^20]

That ADR is reflected directly in code:

1. `RawOpenAIChatClient` is the low-level OpenAI Responses client with a strong warning not to use it directly unless you deliberately layer **FunctionInvocationLayer -> ChatMiddlewareLayer -> ChatTelemetryLayer** in that order.[^21]
2. `OpenAIChatClient` is the fully layered version that inherits those layers on top of `RawOpenAIChatClient`.[^22]
3. `RawFoundryChatClient` adapts a Foundry project endpoint/client into an OpenAI-compatible raw client and tells users to prefer `FoundryChatClient` for middleware, telemetry, and function invocation.[^23]
4. `FoundryChatClient` is the fully layered client, again mixing function invocation, middleware, and telemetry over the raw Foundry client.[^24]

This is a clean and consistent pattern: every provider can expose a raw surface for extension and a production surface with the standard layers already applied.

### Python: Foundry has both chat-client and remote-agent stories

The Python Foundry surface is broader than just "talk to a model." `RawFoundryAgent` and `FoundryAgent` connect to existing PromptAgents or HostedAgents in Foundry. `RawFoundryAgent` builds a client of type `RawFoundryAgentChatClient` (or a subclass) and then wraps it as a local agent; `FoundryAgent` adds the standard middleware and telemetry layers and is explicitly documented as the recommended production class.[^25]

This distinction matters because it shows Agent Framework is not just abstracting model providers; it is also abstracting remote, service-hosted agents as first-class participants in the same agent/workflow model.[^20][^25]

## Workflow and orchestration model

### .NET workflows are graph-based and event-driven

The root README markets graph-based workflows with streaming, checkpointing, human-in-the-loop, and time-travel capabilities, and the implementation backs that up.[^2] In code, `WorkflowBuilder.Build(...)` validates and assembles a `Workflow` object containing executor bindings, edges, request ports, and output executors.[^26] The `Workflow` type can reflect its edges, ports, and executors, exposes a start executor ID, and guards ownership/reuse so the same workflow is not accidentally driven by multiple owners at once without reset semantics.[^7]

At node level, `Executor` is the core processing primitive. It configures a protocol via `ProtocolBuilder`, routes messages through a `MessageRouter`, emits workflow events such as `ExecutorInvokedEvent` / `ExecutorCompletedEvent` / `ExecutorFailedEvent`, and integrates with workflow telemetry.[^27] At run level, `Run` captures emitted `WorkflowEvent`s and supports `ResumeAsync(...)` with external responses or messages, while `StreamingRun` exposes `WatchStreamAsync(...)`, response injection, cancellation, and `RunToCompletionAsync(...)` helper behavior.[^28]

The net effect is that .NET workflows are a real runtime with explicit graph state and run handles, not just helper methods for chaining agents.

### Python orchestrations package provides high-level patterns

Python reaches similar ends through a more opinionated orchestration package:

1. **SequentialBuilder** wires a chain of participants, normalizes input into a shared `list[Message]` conversation, auto-wraps agents via `AgentExecutor`, and can enable request-info pauses for human review between agent steps.[^29]
2. **ConcurrentBuilder** fans a request out to multiple participants and fans responses back into either a default conversation aggregator or a custom callback/executor aggregator.[^30]
3. **GroupChatOrchestrator** is a centralized, orchestrator-directed multi-agent conversation loop driven by a pluggable speaker-selection function and shared `GroupChatState`.[^31]
4. **Handoff** is the decentralized counterpart, where agents themselves choose the next specialist by invoking handoff tools that middleware intercepts and translates into deterministic routing decisions.[^32]
5. **MagenticOrchestrator** adds planning, progress ledgers, re-planning, loop/stall detection, and optional human plan signoff.[^33]

This is one of the clearest cross-language differences in the repo. .NET emphasizes a lower-level graph runtime; Python ships a richer catalog of named orchestration patterns on top of its workflow substrate.

## Hosting and protocol surfaces

### .NET hosting: OpenAI-compatible routes, A2A, and OpenAI interop

The .NET hosting story is wide. `AddOpenAIResponses(...)` registers JSON configuration plus in-memory conversation storage, conversation indexing, a default in-memory responses service, and a hosted-agent response executor.[^34] `MapOpenAIResponses(...)` can expose generic `/v1/responses` endpoints or agent-specific `/{agentName}/v1/responses` routes with create/get/cancel/delete/list-input-items handlers.[^35]

For agent-to-agent protocols, `AIAgentExtensions.MapA2A(...)` bridges an `AIAgent` into A2A task/message flows. It decides whether to run in background, stores continuation tokens for long-running operations in task metadata, creates lightweight `AgentMessage` responses for immediate completions, and upgrades to full `AgentTask` objects when the agent returns a continuation token.[^36] The ASP.NET Core route builder then exposes those A2A task managers behind route groups and optional agent-card discovery surfaces.[^37]

There is also a useful OpenAI SDK interoperability layer: `AIAgentWithOpenAIExtensions` lets callers run an `AIAgent` using native OpenAI `ChatMessage` or `ResponseItem` collections and convert the resulting `AgentResponse` back into native OpenAI `ChatCompletion`, `ResponseResult`, or streaming update types.[^38]

### Python hosting: MCP export, Durable Task clients, and Azure Functions

Python has no single hosting abstraction that does everything, but it has several strong surfaces. At the agent level, `as_mcp_server()` exports an agent as an MCP server.[^19] At the durable-agent layer, `DurableAIAgentClient` wraps a `TaskHubGrpcClient` for external callers, while `DurableAIAgentOrchestrationContext` wraps an orchestration context for internal durable orchestrations.[^39]

The most opinionated production host is `AgentFunctionApp` in `agent-framework-azurefunctions`. Its constructor can register agents directly or extract them from a workflow, optionally enable health checks and MCP tool triggers, and set up workflow orchestration routes when a workflow is provided.[^40] For each registered agent, `_setup_http_run_route(...)` creates `POST /agents/{agentName}/run`, uses thread IDs for session continuity, supports "wait for response" vs accepted/background behavior, signals the agent entity, and returns thread IDs in the response path/header handling.[^41] `_setup_mcp_tool_trigger(...)` can additionally expose each agent as an Azure Functions-native MCP tool trigger with `query` and optional `threadId` parameters.[^42]

## Durable agents

### The durable-agent model is shared across both languages

The durable-agents documentation is one of the most useful docs in the repo because it explains the common mental model: a durable agent persists conversation history and execution state in external storage; each agent session maps to a durable entity/virtual actor; the entity loads state, invokes the underlying agent with full history, appends request/response, and persists updated state; serialized access to each entity avoids race conditions.[^6]

That same doc also makes the key shared identity rule explicit: `AgentSessionId` is composed of agent name plus unique session key, and maps to a durable entity ID with a `dafx-` prefix across both .NET and Python.[^6] The shared JSON schema in `schemas/durable-agent-entity-state.json` reinforces the cross-language contract by defining conversation history entries, typed content items (text, reasoning, tool calls/results, hosted files/vector stores, errors, usage), request metadata, response usage, and the top-level `schemaVersion` + `data.conversationHistory` envelope.[^43]

### .NET durable implementation

On the .NET side, `DurableAIAgent` is the orchestration-time durable agent wrapper. It creates sessions using the orchestration context, serializes/deserializes `DurableAgentSession`, rejects arbitrary session types, and routes `RunCoreAsync(...)` through `CallEntityAsync<AgentResponse>(sessionId, nameof(AgentEntity.Run), request)`. It explicitly does **not** support cancellation and does not implement true streaming; `RunCoreStreamingAsync(...)` just replays the full response as a single update sequence.[^44]

`DurableAIAgentProxy` is the external caller counterpart. It creates `RunRequest`s, delegates execution to `IDurableAgentClient`, supports fire-and-forget via `DurableAgentRunOptions.IsFireAndForget`, and otherwise waits for an `AgentRunHandle` to return a response.[^45]

The real durable host is `AgentEntity`. It loads the underlying registered `AIAgent` by name, appends each request to durable conversation history, logs request/response messages, sets a `DurableAgentContext` for tool access during execution, runs the inner agent as a stream, optionally forwards updates to `IAgentResponseHandler`, persists the final response, and manages per-agent TTL by updating expiration time and self-scheduling `CheckAndDeleteIfExpired` signals.[^46] `AgentSessionId` is the strongly typed identity object that enforces the `dafx-` naming convention and supports round-tripping through `EntityInstanceId`, strings, and JSON.[^47]

For Azure Functions hosting, `FunctionsApplicationBuilderExtensions.ConfigureDurableAgents(...)` wires the durable-agent services, ensures shared durable options, registers metadata transformers, and installs middleware that handles built-in HTTP, MCP tool, and entity function execution paths.[^48]

### Python durable implementation

Python's durable stack is structurally similar but packaged differently. The `agent-framework-durabletask` package exports the durable client, worker, entity logic, models, orchestration context, shim types, and durable state models; Azure Functions integration lives separately in `agent-framework-azurefunctions`.[^49]

`DurableAIAgentWorker` wraps a `TaskHubGrpcWorker`, registers named agents as durable entities (`dafx-{agent_name}`), and generates a per-agent entity class whose `run(...)` method invokes the shared `AgentEntity` logic and returns serialized response dictionaries.[^50] `DurableAIAgentClient` wraps `TaskHubGrpcClient` for external callers and returns `DurableAIAgent` shims that execute through a `ClientAgentExecutor`.[^39]

The shared Python `AgentEntity` is especially interesting because it is platform-agnostic execution logic. It appends each request into durable conversation history, reconstructs replayable messages while intentionally dropping reasoning content, tries to execute the underlying agent in streaming mode first, optionally emits update callbacks, persists final responses, and on exception persists an assistant error response back into durable state rather than silently discarding failure information.[^51]

Under the hood, `ClientAgentExecutor` signals the entity, then either returns an acceptance response immediately for fire-and-forget requests or polls entity state for the matching correlation ID.[^52] `RunRequest` transports the message, response-format descriptor, tool-enable flag, wait-for-response flag, orchestration ID, correlation ID, and arbitrary forwarded options; `AgentSessionId` again uses the `dafx-` prefix convention and supports both `@name@key` parsing and simple key parsing when the agent name is already known.[^53]

Azure Functions reuses that shared entity logic through `create_agent_entity(...)`, which plugs an Azure Durable Entity context into `AgentEntityStateProviderMixin`, dispatches `run`/`run_agent`/`reset` operations, and persists results or errors via the Azure Durable Functions entity state APIs.[^54]

### Durable agents are deliberately not "true streaming"

One subtle but important design point: durable agents are not pretending to offer full end-to-end streaming over entity operations. The docs say durable agents are request/response at the entity layer and therefore rely on **response callbacks** for "reliable streaming"; the .NET `DurableAIAgent` implementation confirms that its streaming API is simulated by turning a complete response into one or more updates after completion.[^6][^44] This is a reasonable design tradeoff, but it is a real limitation users need to understand.

## Design ADRs worth knowing

Several ADRs materially explain why the code looks the way it does:

1. **ADR 0001 - Agent run responses**: distinguishes primary vs secondary output and argues for a simple non-streaming experience while preserving mixed-event streaming semantics.[^55]
2. **ADR 0002 - Agent tools**: chooses a hybrid tool model where generic abstractions, provider-specific `AITool` types, and raw-provider escape hatches can all coexist.[^56]
3. **ADR 0003 - OpenTelemetry instrumentation**: explicitly chooses a wrapper/delegation model for agent telemetry rather than embedding telemetry into core agent classes.[^57]
4. **ADR 0015 - Agent run context**: explains why `.NET` run context is explicit in core methods but ambient via `AsyncLocal` for tools and middleware.[^11]
5. **ADR 0021 - Provider-leading clients**: explains the Python package split and why Foundry becomes a first-class agent surface rather than only a raw client.[^20]

If you are evaluating maintainability, these ADRs are a positive signal: the repository is documenting design tradeoffs, not just accreting code.

## CodeAct / Hyperlight

CodeAct is clearly an active frontier in this repo rather than a fully settled feature. Both the Python and .NET implementation docs describe the same core idea: enable CodeAct through a context-provider integration, expose a provider-owned CodeAct tool registry distinct from the agent's direct tool surface, use `execute_code` as the model-facing tool, and support portable sandbox capabilities such as file mounts and outbound network allow-lists.[^10]

The most important design choice in both languages is separation of surfaces: CodeAct-managed tools are **not** inferred from the agent's direct tool list. Instead, the provider owns its own registry, can snapshot that registry at run start for determinism, and computes approval behavior for `execute_code` from its own configuration.[^10] In both languages, Hyperlight is the initial backend, and Python package status marks `agent-framework-hyperlight` as `alpha`, which is consistent with the still-evolving shape of the feature.[^3]

This means CodeAct is promising, but I would currently categorize it as an advanced/experimental subsystem rather than the stable center of the framework.

## Package and maturity picture

The clearest maturity signal in the repo is Python's `PACKAGE_STATUS.md`. Today, the meta-package `agent-framework`, `agent-framework-core`, `agent-framework-foundry`, and `agent-framework-openai` are marked `released`; many integrations and orchestration/hosting packages are `beta`; and `agent-framework-gemini` plus `agent-framework-hyperlight` are `alpha`.[^3] That staging matters because some of the most interesting features in the repo, including durable-task hosting, orchestration helpers, and CodeAct-related work, sit outside the most stable tier.[^3]

On top of that, the top-level Python workspace uses strict development tooling (`uv`, `ruff`, `pytest`, `mypy`, `pyright`, OpenTelemetry tooling) and even carries an explicit `litellm<1.82.7` constraint to avoid compromised releases.[^58] That is another signal that the repo is being run as a serious multi-package engineering effort.

## Strengths, tradeoffs, and who this repo is for

### Main strengths

1. **Broad coverage**: few open-source agent frameworks try to cover local agents, hosted/remote agents, graph workflows, A2A, OpenAI-compatible hosting, durable execution, Azure Functions hosting, MCP exposure, and multi-provider integrations in one repo.[^2][^6][^8]
2. **Thoughtful layering**: both languages use strong composition patterns rather than burying all behavior in monoliths. .NET uses explicit wrappers/builders; Python uses raw-vs-layered clients and agents.[^15][^21][^22]
3. **Cross-language conceptual alignment**: the durable-agent identity model, provider-layering approach, and tool/context-centric execution story are recognizably aligned across .NET and Python, even where the exact APIs differ.[^6][^20][^43]
4. **Good documentation for advanced topics**: the ADR set, durable-agent docs, and CodeAct docs are unusually helpful for understanding intent rather than just syntax.[^6][^10][^11][^55]

### Main tradeoffs / risks

1. **Large conceptual surface**: "Agent Framework" includes agents, tools, workflows, hosting, observability, durable entities, remote agents, MCP, A2A, and experimental labs. That breadth is a strength for platform teams, but a learning cost for ordinary application developers.[^2][^6]
2. **Maturity is uneven**: the stable core exists, but a lot of high-value integrations are still beta or alpha.[^3]
3. **Durable streaming is indirect**: durable agents use callbacks/replay rather than true streaming over durable entity operations.[^6][^44]
4. **Language parity is conceptual, not exact**: .NET leans on a graph runtime and wrappers; Python leans on layered agents/clients and a richer catalog of orchestration patterns. That is fine, but teams using both languages should not expect perfect API symmetry.[^7][^29][^30][^31][^32][^33]

### Best-fit users

This repo looks best suited for:

- teams building **multi-agent or workflow-heavy systems** rather than single-prompt utilities;[^2][^7]
- teams that need **multiple hosting surfaces** (HTTP, A2A, MCP, Azure Functions, Durable Task);[^6][^8]
- teams comfortable adopting a **framework platform**, not just a tiny inference wrapper;[^2]
- organizations already leaning into **Azure + Foundry + Durable Task**, while still wanting multi-provider escape hatches.[^23][^24][^36][^40]

If a team only needs a very small agent abstraction over one provider, this repo may be more framework than they need.

## Bottom line

`microsoft/agent-framework` is a substantial, fast-moving agent platform with real architectural depth. The durable-agent implementation is especially notable: it is not a toy sample, but a cross-language, entity-backed subsystem with shared identity rules and state shape.[^6][^43][^44][^51] The core agent runtimes in both languages are well structured and clearly designed for composition, and the hosting story is broader than most competing repos.[^12][^14][^34][^35][^36][^40]

The main caution is scope: this repo is trying to be a lot of things at once, and some of the most ambitious surfaces are still maturing. But if the question is whether `microsoft/agent-framework` is a serious framework worth studying for agent architecture, orchestration, hosting, and durable execution patterns, the answer is clearly **yes**.[^2][^3][^6]

## Confidence

**High confidence** on repository scope, runtime architecture, durable-agent design, Python package maturity, and the concrete behavior of the cited .NET/Python implementations, because those claims are grounded directly in source files, ADRs, and feature docs from the analyzed snapshot.

**Moderate confidence** on longer-term direction and roadmap prioritization beyond what is documented, because the repo is moving quickly and some ADRs/features are still in proposed or beta/alpha states.

## Footnotes

[^1]: Snapshot `b03cb324d5cc5e91a55b5eb9045b8ead244aaf11`. `sources/microsoft-agent-framework/repo/README.md:3-10,24-80,159-176`; `sources/microsoft-agent-framework/repo/python/pyproject.toml:1-27,29-94`; `sources/microsoft-agent-framework/repo/python/PACKAGE_STATUS.md:1-43`.
[^2]: `sources/microsoft-agent-framework/repo/README.md`, lines 3-10, 24-80, 159-176.
[^3]: `sources/microsoft-agent-framework/repo/python/pyproject.toml`, lines 1-27, 29-94; `sources/microsoft-agent-framework/repo/python/PACKAGE_STATUS.md`, lines 1-43.
[^4]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Abstractions/AIAgent.cs`, lines 16-40, 96-106, 251-342.
[^5]: `sources/microsoft-agent-framework/repo/python/packages/core/agent_framework/_agents.py`, lines 578-767, 889-967, 985-1148, 1150-1450, 1452-1573, 1584-1712.
[^6]: `sources/microsoft-agent-framework/repo/docs/features/durable-agents/README.md`, lines 1-18, 19-38, 39-69, 70-123, 125-205, 207-219.
[^7]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows/Workflow.cs`, lines 17-104, 106-198, 210-230; `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs`, lines 598-614.
[^8]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.OpenAI/ServiceCollectionExtensions.cs`, lines 14-77; `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.OpenAI/EndpointRouteBuilderExtensions.Responses.cs`, lines 16-160; `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.A2A/AIAgentExtensions.cs`, lines 17-156; `sources/microsoft-agent-framework/repo/python/packages/azurefunctions/agent_framework_azurefunctions/_app.py`, lines 182-260, 698-857, 858-1010.
[^9]: `sources/microsoft-agent-framework/repo/python/pyproject.toml`, lines 62-93; `sources/microsoft-agent-framework/repo/python/PACKAGE_STATUS.md`, lines 13-43.
[^10]: `sources/microsoft-agent-framework/repo/docs/features/code_act/python-implementation.md`, lines 1-23, 44-138, 139-220; `sources/microsoft-agent-framework/repo/docs/features/code_act/dotnet-implementation.md`, lines 1-23, 43-129, 131-220; `sources/microsoft-agent-framework/repo/python/PACKAGE_STATUS.md`, lines 31-36.
[^11]: `sources/microsoft-agent-framework/repo/docs/decisions/0015-agent-run-context.md`, lines 10-18, 27-34, 72-117, 142-147.
[^12]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs`, lines 17-37, 47-144, 146-200, 203-260.
[^13]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgentRunOptions.cs`, lines 8-67.
[^14]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs`, lines 13-18, 23-38, 52-89; `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientBuilderExtensions.cs`, lines 88-116.
[^15]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/AIAgentBuilder.cs`, lines 13-18, 40-70, 72-93.
[^16]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/FunctionInvocationDelegatingAgent.cs`, lines 13-18, 24-31, 33-68, 70-86.
[^17]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI/OpenTelemetryAgent.cs`, lines 13-20, 22-31, 47-57, 80-107, 109-145, 166-205.
[^18]: `sources/microsoft-agent-framework/repo/python/packages/core/agent_framework/_tools.py`, lines 212-257, 269-360.
[^19]: `sources/microsoft-agent-framework/repo/python/packages/core/agent_framework/_agents.py`, lines 1452-1573.
[^20]: `sources/microsoft-agent-framework/repo/docs/decisions/0021-provider-leading-clients.md`, lines 9-42, 43-73.
[^21]: `sources/microsoft-agent-framework/repo/python/packages/openai/agent_framework_openai/_chat_client.py`, lines 247-264, 361-427.
[^22]: `sources/microsoft-agent-framework/repo/python/packages/openai/agent_framework_openai/_chat_client.py`, lines 2552-2559, 2563-2668.
[^23]: `sources/microsoft-agent-framework/repo/python/packages/foundry/agent_framework_foundry/_chat_client.py`, lines 108-124, 129-212.
[^24]: `sources/microsoft-agent-framework/repo/python/packages/foundry/agent_framework_foundry/_chat_client.py`, lines 464-475, 519-574.
[^25]: `sources/microsoft-agent-framework/repo/python/packages/foundry/agent_framework_foundry/_agent.py`, lines 447-567, 625-743.
[^26]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows/WorkflowBuilder.cs`, lines 567-595, 598-614.
[^27]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows/Executor.cs`, lines 160-217, 218-279.
[^28]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows/Run.cs`, lines 13-35, 38-95, 97-135; `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Workflows/StreamingRun.cs`, lines 13-25, 31-45, 66-125.
[^29]: `sources/microsoft-agent-framework/repo/python/packages/orchestrations/agent_framework_orchestrations/_sequential.py`, lines 3-38, 65-107, 109-167, 194-225, 227-240.
[^30]: `sources/microsoft-agent-framework/repo/python/packages/orchestrations/agent_framework_orchestrations/_concurrent.py`, lines 24-42, 45-71, 73-138, 140-178, 180-236.
[^31]: `sources/microsoft-agent-framework/repo/python/packages/orchestrations/agent_framework_orchestrations/_group_chat.py`, lines 3-19, 63-85, 88-161, 163-238.
[^32]: `sources/microsoft-agent-framework/repo/python/packages/orchestrations/agent_framework_orchestrations/_handoff.py`, lines 3-30, 80-117, 122-145, 192-260.
[^33]: `sources/microsoft-agent-framework/repo/python/packages/orchestrations/agent_framework_orchestrations/_magentic.py`, lines 850-867, 869-950, 952-1010.
[^34]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.OpenAI/ServiceCollectionExtensions.cs`, lines 14-60.
[^35]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.OpenAI/EndpointRouteBuilderExtensions.Responses.cs`, lines 21-41, 58-106, 109-159.
[^36]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.A2A/AIAgentExtensions.cs`, lines 17-25, 37-73, 111-156, 158-209, 219-256.
[^37]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.A2A.AspNetCore/EndpointRouteBuilderExtensions.cs`, lines 18-38, 47-76, 91-110, 125-173, 189-220.
[^38]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.OpenAI/Extensions/AIAgentWithOpenAIExtensions.cs`, lines 13-24, 42-50, 68-76, 78-130.
[^39]: `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/_client.py`, lines 3-18, 23-27, 49-72, 73-92; `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/_orchestration_context.py`, lines 3-17, 21-26, 48-76.
[^40]: `sources/microsoft-agent-framework/repo/python/packages/azurefunctions/agent_framework_azurefunctions/_app.py`, lines 112-174, 182-260.
[^41]: `sources/microsoft-agent-framework/repo/python/packages/azurefunctions/agent_framework_azurefunctions/_app.py`, lines 698-797, 1213-1295.
[^42]: `sources/microsoft-agent-framework/repo/python/packages/azurefunctions/agent_framework_azurefunctions/_app.py`, lines 858-1010.
[^43]: `sources/microsoft-agent-framework/repo/schemas/durable-agent-entity-state.json`, lines 1-217.
[^44]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.DurableTask/DurableAIAgent.cs`, lines 13-18, 31-40, 49-76, 89-145, 147-172, 187-196.
[^45]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.DurableTask/DurableAIAgentProxy.cs`, lines 8-12, 14-39, 41-100.
[^46]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.DurableTask/AgentEntity.cs`, lines 13-24, 32-79, 83-157, 160-215, 217-232.
[^47]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.DurableTask/AgentSessionId.cs`, lines 9-18, 21-58, 98-119, 123-168.
[^48]: `sources/microsoft-agent-framework/repo/dotnet/src/Microsoft.Agents.AI.Hosting.AzureFunctions/FunctionsApplicationBuilderExtensions.cs`, lines 18-53, 55-91, 109-148.
[^49]: `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/__init__.py`, lines 3-12, 24-52, 59-108.
[^50]: `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/_worker.py`, lines 24-52, 53-114, 143-203.
[^51]: `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/_entities.py`, lines 35-81, 84-94, 126-193, 195-208, 210-277.
[^52]: `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/_executors.py`, lines 37-45, 106-183, 184-206, 209-260.
[^53]: `sources/microsoft-agent-framework/repo/python/packages/durabletask/agent_framework_durabletask/_models.py`, lines 35-50, 53-93, 96-176, 178-215, 218-260.
[^54]: `sources/microsoft-agent-framework/repo/python/packages/azurefunctions/agent_framework_azurefunctions/_entities.py`, lines 28-49, 51-125.
[^55]: `sources/microsoft-agent-framework/repo/docs/decisions/0001-agent-run-response.md`, lines 11-52, 73-90, 95-129, 163-185, 187-203, 205-220.
[^56]: `sources/microsoft-agent-framework/repo/docs/decisions/0002-agent-tools.md`, lines 11-23, 24-110, 114-129.
[^57]: `sources/microsoft-agent-framework/repo/docs/decisions/0003-agent-opentelemetry-instrumentation.md`, lines 9-23, 58-87, 88-121, 142-144.
[^58]: `sources/microsoft-agent-framework/repo/python/pyproject.toml`, lines 29-60, 95-140.
