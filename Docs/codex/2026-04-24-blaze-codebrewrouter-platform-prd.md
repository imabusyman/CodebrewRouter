# PRD: Blaze / CodebrewRouter Platform

## Document Control

- Date: 2026-04-24
- Status: Draft for planning
- Product: Blaze intelligent LLM platform
- Primary logical model: `codebrewRouter`
- Repository: `C:\src\CodebrewRouter`
- Intended storage location: `C:\src\CodebrewRouter\Docs\codex`

## Executive Summary

Blaze is evolving from a .NET 10 LLM gateway into a broader product platform that presents one OpenAI-compatible contract to clients while deciding where and how inference should run. The product vision is to combine:

- a connected server-side gateway for cloud, LAN, and enterprise model routing
- a policy-driven virtual model layer exposed as `codebrewRouter`
- an offline-capable edge runtime for mobile and disconnected environments

The core product promise is simple: an application should call one model and one API shape, while Blaze decides whether work should run on Azure Foundry, Foundry Local, Ollama, another configured provider, or a compact on-device model when the device is offline.

This PRD defines the product requirements for that platform, using the current repository as the starting point and extending it into a target architecture that supports LiteLLM-style compatibility, routing policy, governance, and offline/mobile execution.

## Product Vision

Blaze is a unified inference access layer for application teams that want:

- a single OpenAI-compatible endpoint
- provider portability without rewriting client code
- resilient routing and failover across cloud and local backends
- a virtual model abstraction that represents policy, not a single provider
- offline/mobile continuity through a compact local runtime

In this vision, `codebrewRouter` is the canonical model identifier that consumers target. It is not a single hosted model. It is a routing contract that selects the best available execution target based on policy, capability, connectivity, cost, latency, and device context.

## Problem Statement

Teams building AI-enabled applications currently face four recurring problems:

1. Provider fragmentation. Each app integrates cloud providers, local runtimes, auth, retries, and tool behavior differently.
2. Online-only architecture. Applications fail or degrade badly when connectivity is poor, unavailable, or too expensive for the task.
3. Weak governance. Teams lack a clean way to apply budgets, model access rules, rate limits, and auditability across multiple providers.
4. Portability gaps. Client code often targets a concrete model or vendor, which makes migration, optimization, and offline support expensive.

For Blaze specifically, the current repository is gateway-first and server-first. It has the beginnings of LiteLLM-style compatibility and routing, but it does not yet provide a full policy model, provider catalog, or an edge runtime that can execute offline on mobile.

## Users and Personas

### Application Developer

Needs one API contract, stable model names, predictable request and response shapes, and minimal client branching by provider.

### Platform Operator

Needs centralized routing, provider configuration, health status, budgets, API keys, policy controls, and observability.

### Product Team

Needs a way to specify behavior in product terms such as fast, cheap, offline-first, private, or tool-enabled rather than binding directly to a provider name.

### Mobile or Field User

Needs core AI experiences to continue when the network is unavailable, weak, expensive, or policy-restricted.

## Current State Snapshot

The repository already provides meaningful building blocks:

- `Blaze.LlmGateway.Api`, `Core`, `Infrastructure`, `AppHost`, `ServiceDefaults`, `Benchmarks`, and `Tests`
- OpenAI-style endpoints for chat, completions, and model listing
- `DelegatingChatClient`-based pipeline usage and routing logic
- Aspire orchestration and basic provider wiring
- automated tests validating current gateway behavior

The main gaps relative to the target product are:

- the routing model is still largely provider-centric instead of catalog and policy-centric
- `codebrewRouter` exists conceptually but is not yet the full virtual-model contract for all environments
- offline/mobile execution does not exist as a first-class runtime
- LiteLLM-style governance features such as key management, budgets, and per-key model access are not complete
- tool calling, MCP integration, and streaming failover are only partially realized
- current request models are not ready for full multimodal and rich tool payload compatibility

## Product Principles

### One Contract, Many Runtimes

Clients integrate once and do not care whether execution happens in the cloud, on the LAN, or on-device.

### Policy Before Provider

Applications target `codebrewRouter` or a named profile, not a hard-coded backend unless they explicitly override it.

### Offline Is First-Class

Disconnected execution is a product feature, not a degraded afterthought.

### Graceful Degradation

If the best target is unavailable, Blaze should choose the next acceptable target rather than fail unnecessarily.

### Observable by Default

Every routing decision, failure, sync event, and usage record should be inspectable.

## Goals

1. Deliver a LiteLLM-style OpenAI-compatible gateway that can serve as the default entry point for Blaze applications.
2. Establish `codebrewRouter` as a virtual model abstraction that encapsulates routing policy and deployment awareness.
3. Support a mobile/offline runtime path through a new Blaze Edge capability that can run a compact local model profile.
4. Make cloud, local, and offline targets part of the same provider and capability catalog.
5. Provide governance features including auth, model access policy, budgets, quotas, and auditability.
6. Preserve the existing .NET and MEAI architecture strengths while making the system easier to extend.

## Non-Goals

- Training or fine-tuning foundation models inside Blaze
- Replacing full MLOps platforms
- Building a general-purpose API gateway unrelated to LLM traffic
- Shipping every possible OpenAI or LiteLLM endpoint in the first phase
- Guaranteeing the same maximum model quality offline as online

## Scope

### In Scope

- OpenAI-compatible inference surface for Blaze clients
- virtual model contract via `codebrewRouter`
- provider and model catalog spanning cloud, local server, and edge runtime targets
- routing policy, fallback, and health-aware orchestration
- offline/mobile execution using a compact local model profile
- usage, governance, and administration needed for a production platform
- tool and agent extensibility through MCP and future agent integrations

### Out of Scope for Initial Delivery

- image generation, speech, and full multimodal parity beyond the core extensible contract
- training pipelines and evaluation harnesses as first-class product pillars
- marketplace features for third-party providers
- cross-tenant billing portal and enterprise invoicing workflows

## Product Architecture

The target product consists of three major surfaces.

### 1. Blaze Gateway

Blaze Gateway is the connected control and routing plane. It exposes OpenAI-compatible endpoints, owns provider configuration, routes requests, enforces policy, and provides observability. This is the direct evolution of the current repository.

Responsibilities:

- API compatibility
- provider and model discovery
- routing decisions and failover
- governance, authentication, quotas, and usage
- tool injection and execution policy
- admin APIs and telemetry

### 2. `codebrewRouter` Virtual Model Layer

`codebrewRouter` is the client-facing logical model identity. It is a policy model that maps a user request and execution context to an actual target.

Examples of execution context:

- online vs offline
- mobile vs server
- latency sensitivity
- cost sensitivity
- tool requirement
- privacy or local-only requirement

The virtual model layer must also support named profiles such as:

- `codebrewRouter`
- `codebrewRouter-fast`
- `codebrewRouter-local-first`
- `codebrewRouter-private`

### 3. Blaze Edge

Blaze Edge is a lightweight runtime or SDK for local execution in offline or intermittent-connectivity environments. It should be embeddable in mobile or edge-hosted applications and able to execute a compact model profile locally.

Initial design assumptions:

- Blaze Edge will support at least one compact local model profile suitable for offline execution.
- The default offline profile may use a Gemma-family compact model or another equivalent small model, depending on runtime compatibility, licensing, and device constraints.
- Blaze Edge should preserve the same logical model contract so apps can still target `codebrewRouter`.

## Core User Flows

### Connected Application Flow

1. App sends an OpenAI-compatible request to Blaze Gateway with `model = codebrewRouter`.
2. Gateway evaluates policy, capabilities, health, and request characteristics.
3. Gateway selects a target provider and executes the request.
4. Gateway streams the response and records structured usage and routing data.

### Offline Mobile Flow

1. App invokes Blaze Edge with `model = codebrewRouter`.
2. Edge detects no acceptable network path or receives a local-first policy.
3. Edge resolves `codebrewRouter` to a compact on-device model profile.
4. Edge runs inference locally, stores local usage and conversation state, and marks events for later sync if needed.

### Reconnected Sync Flow

1. Device regains connectivity.
2. Blaze Edge syncs policy manifests, model availability metadata, and deferred usage records with Blaze Gateway.
3. Local state is reconciled according to privacy and retention policies.

## Functional Requirements

### FR-01 API Compatibility

1. Blaze must expose an OpenAI-compatible API surface for core text generation workflows.
2. Blaze must support `POST /v1/chat/completions` and `GET /v1/models` as minimum baseline endpoints.
3. Blaze should preserve or add compatible support for `POST /v1/completions` where needed for legacy clients.
4. Blaze must support streaming responses using SSE where the target provider or edge runtime supports streaming.
5. Blaze request and response DTOs must be typed, versionable, and extensible enough to support tools, multimodal content parts, and provider metadata later.
6. Blaze must document its compatibility profile clearly, including any intentionally unsupported fields.

### FR-02 Virtual Model and Policy Layer

1. `codebrewRouter` must be a first-class model identifier recognized by both gateway and edge runtimes.
2. A request targeting `codebrewRouter` must resolve to a concrete target based on a policy engine, not a hard-coded provider.
3. Blaze must support named routing profiles with independent policy settings.
4. The policy engine must consider capability requirements, connectivity, latency, cost, availability, and explicit overrides.
5. Blaze must provide a way to explain or inspect the routing decision for debugging and governance.

### FR-03 Provider and Model Catalog

1. Blaze must maintain a normalized catalog of providers, models, capabilities, health, and deployment metadata.
2. The catalog must include cloud providers, local server providers, and edge-local model profiles.
3. Models must carry metadata such as context window, tool support, offline eligibility, cost class, and privacy class where available.
4. The catalog must support dynamic enablement and disablement of specific targets without changing application code.
5. The model catalog must power both routing and operator-facing discovery surfaces.

### FR-04 Routing, Failover, and Resilience

1. Blaze Gateway must support ordered fallback chains for virtual and concrete models.
2. Gateway routing must include provider health awareness and circuit-breaking.
3. Gateway must detect throttling, network errors, and auth misconfiguration and react appropriately.
4. Gateway must support pre-stream failover when the initial target fails before tokens are emitted.
5. Gateway must record structured failure reasons and fallback outcomes.
6. Routing overhead must remain small enough that it does not dominate model latency.

### FR-05 Edge Runtime and Offline Execution

1. Blaze Edge must support local inference without requiring a round trip to Blaze Gateway.
2. Blaze Edge must expose the same logical model contract used by the gateway.
3. Blaze Edge must support at least one compact local model profile for offline use.
4. Blaze Edge must support local conversation continuity while disconnected.
5. Blaze Edge must allow application hosts to prefer offline, prefer online, or automatically choose based on policy.
6. Blaze Edge must tolerate intermittent connectivity and resume sync cleanly when online returns.
7. Blaze Edge must provide lightweight diagnostics about model availability, storage use, warm-state, and sync state.

### FR-06 Mobile Environment Support

1. The product must define a supported mobile strategy for offline execution.
2. Blaze Edge must be consumable from a mobile host application through an SDK, local service layer, or equivalent embedded contract.
3. Mobile support must account for device storage, CPU or NPU availability, battery impact, and download size.
4. Mobile hosts must be able to determine whether the local model profile is installed, ready, or needs download or update.
5. Mobile flows must continue to work with `codebrewRouter` even when the cloud is unreachable.

### FR-07 Sync, State, and Portability

1. Blaze Edge must be able to sync selected metadata back to Blaze Gateway when online.
2. Syncable data may include usage records, policy manifest updates, health or readiness state, and optionally conversation summaries.
3. Privacy-sensitive data must be configurable so local prompt content is not uploaded by default unless policy allows it.
4. Blaze must define durable identifiers for devices, sessions, and routing profiles where needed.
5. Blaze must support local-only operation for deployments that forbid sync.

### FR-08 Governance, Authentication, and Budgets

1. Blaze Gateway must support API keys and optionally bearer-token auth.
2. Blaze must support virtual keys or policy-bound credentials similar to LiteLLM-style governance patterns.
3. Operators must be able to restrict which models or routing profiles a client may use.
4. Operators must be able to define request budgets, token budgets, and rate limits by client, app, or tenant.
5. Governance outcomes must be enforced consistently whether the request resolves to a cloud provider or an allowed local target.

### FR-09 Tooling and Agent Extensibility

1. Blaze must support tool-enabled workflows using MCP-aligned abstractions.
2. Tool availability must be policy-aware and target-aware.
3. Tool definitions must flow through the request contract cleanly enough for future function-calling parity.
4. Blaze should support future agent and workflow surfaces without breaking the main API contract.

### FR-10 Administration and Observability

1. Operators must be able to inspect providers, model profiles, health, policy, and routing outcomes.
2. Blaze must emit traces, metrics, and structured logs for request execution and routing behavior.
3. Usage reporting must distinguish between cloud, local server, and edge-local execution.
4. Blaze must provide enough observability to answer why a request chose a target and what it cost.
5. Blaze should provide admin endpoints or dashboards for provider configuration, health, and policy.

## Detailed Requirement Expansion

This section expands the product requirements using the current repository state, the existing architecture and ADR docs, and targeted external research where it materially affects product direction.

### Current Codebase Baseline

The current repo already establishes a useful implementation baseline that the detailed requirements should respect:

- `Blaze.LlmGateway.Api` already exposes `POST /v1/chat/completions`, `POST /v1/completions`, `GET /v1/models`, OpenAPI, Scalar, and health endpoints.
- `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` are already wired in the Api host, so the connected gateway baseline includes standard Aspire health and service-default behavior.
- `InfrastructureServiceExtensions` currently registers `AzureFoundry`, `FoundryLocal`, `OllamaLocal`, and a keyed virtual client named `CodebrewRouter`.
- `CodebrewRouterChatClient` already implements a real task-classified fallback chain using `CodebrewRouterOptions.FallbackRules`.
- `ChatCompletionsEndpoint` currently converts requests into MEAI `ChatMessage` and `ChatOptions`, but only for text content and without flowing request `tools` into `ChatOptions.Tools`.
- `OpenAiModels.ChatMessageDto` currently models `content` as a scalar string, which blocks multimodal payloads even though MEAI can represent rich content parts.
- `LlmRoutingChatClient` supports non-streaming failover today, but its streaming failover helper is not used by the active streaming path.
- `ModelCatalogService` currently merges Azure-discovered models with configured `FoundryLocal` and virtual `codebrewRouter`, which is enough for a baseline but not enough for the future provider catalog.
- MCP infrastructure exists in the repo but is intentionally disabled in `Program.cs`, so tool-plane requirements must distinguish between "supported by architecture" and "live in runtime."

The detailed requirements below intentionally separate hardening work that the current code clearly needs from future work that extends the product into the edge and mobile space.

### Expanded API and Wire-Compatibility Requirements

#### REQ-API-01 OpenAI-correct chat wire format

Blaze must emit OpenAI-correct chat object identifiers and streaming chunk shapes for supported endpoints. This includes:

- `chat.completion` for non-streaming chat responses
- `chat.completion.chunk` for streaming chat responses
- assistant role emission on the first delta chunk
- a terminal chunk carrying the appropriate `finish_reason`
- `data: [DONE]` termination for SSE streams

This is a product requirement, not just an implementation fix, because OpenAI-compatibility is the primary client contract.

#### REQ-API-02 Typed request and response DTOs

All northbound request and response bodies must be modeled with typed DTOs rather than ad hoc document parsing. DTOs must be versionable and able to represent:

- text-only message content
- multimodal content parts
- tool declarations and future tool choice metadata
- model metadata and usage objects
- provider-neutral extension fields where necessary

This requirement is grounded in the existing `OpenAiModels` file and should drive a contract hardening pass rather than a redesign from scratch.

#### REQ-API-03 Multimodal-ready message contract

Chat message content must support both:

- a scalar text form for simple client requests
- an array-of-content-parts form for richer requests

The translation layer must preserve enough fidelity to support text plus image input in the future. This is essential for the Yardly-style vision scenario discussed in repo planning notes and should be treated as phase-1 gateway hardening, not a distant enhancement.

#### REQ-API-04 Tool passthrough into MEAI

If a client sends OpenAI-style tools or function declarations, Blaze must translate them into the MEAI tool model used by the active `IChatClient` pipeline. A tool-enabled request must not silently degrade into a plain text request.

At minimum, the requirements are:

- request `tools` must be validated
- supported tool definitions must be translated into `ChatOptions.Tools`
- unsupported tool constructs must produce a clear compatibility error instead of being ignored

#### REQ-API-05 Supported endpoint profile

Blaze must publish a clear compatibility profile defining which endpoints are:

- required in v1
- optional in v1
- explicitly deferred

Based on the current repo and planning docs, the required minimum profile is:

- `POST /v1/chat/completions`
- `GET /v1/models`
- `POST /v1/completions` when legacy compatibility is desired

`/v1/responses`, `/v1/embeddings`, and broader modality endpoints should remain explicitly staged rather than implied.

#### REQ-API-06 Error compatibility

Blaze must return structured, OpenAI-style error envelopes for validation, auth, quota, policy, and provider failures. Compatibility includes both schema and behavior:

- 400 for invalid request shape or unsupported field combinations
- 401 or 403 for auth and policy denials
- 429 for rate limit or quota exhaustion
- 5xx only for genuine service failures

### Expanded Provider and Model Catalog Requirements

#### REQ-CAT-01 Catalog replaces enum-centric routing

The long-term product must move from a small enum-plus-options model to a normalized provider and model catalog. The current `LlmGatewayOptions` layout is acceptable as a transitional form, but the requirements should target:

- provider descriptors
- model profiles
- capability metadata
- locality metadata
- health and enabled state
- pricing or cost-class metadata where available

This aligns with the repo ADRs and resolves the mismatch between current implementation and planned extensibility.

#### REQ-CAT-02 Catalog must represent three execution localities

Every routable target must be classifiable as one of:

- cloud
- local server or LAN
- edge-local or on-device

This is necessary because the future Blaze product is not merely multi-provider. It is multi-locality, and locality directly affects privacy, latency, cost, and offline behavior.

#### REQ-CAT-03 Capability metadata is a routing primitive

Each model profile must be able to express at least:

- context window
- streaming support
- tool-call support
- vision support
- offline eligibility
- cost class
- privacy class or local-only suitability
- expected runtime family or compatibility mode

The routing and policy layers must treat this metadata as first-class input instead of relying solely on model-name conventions.

#### REQ-CAT-04 Startup validation and configuration safety

Catalog configuration must be validated on startup so Blaze can catch misconfigurations such as:

- routing rules that reference non-existent providers
- duplicate model IDs
- invalid fallback chains
- provider keys missing required endpoint information
- a virtual profile that resolves only to disabled or absent targets

This requirement is directly motivated by the current code path where `CodebrewRouterOptions` can reference `GithubModels` even when no corresponding keyed client is actually registered.

#### REQ-CAT-05 Discovery and configured-model coexistence

The catalog must support both:

- discovered models from compatible providers
- manually configured model profiles and virtual profiles

This requirement preserves the existing Azure discovery path while allowing local or edge targets to be described explicitly.

### Expanded `codebrewRouter` Requirements

#### REQ-CBR-01 `codebrewRouter` is the primary logical contract

`codebrewRouter` must be treated as the default logical model identity for Blaze-enabled applications. Product documentation, SDK defaults, and admin tooling should assume that most clients target `codebrewRouter` unless they have a strong reason to pin a concrete model.

#### REQ-CBR-02 Policy-driven target resolution

`codebrewRouter` resolution must be driven by a policy engine that can consider:

- task type or request intent
- model capability needs
- current provider health
- locality policy
- online or offline state
- user or client entitlements
- explicit override inputs

The current task-classifier implementation is a valid starting point, but the requirement is broader than classification alone.

#### REQ-CBR-03 Named profile variants

Blaze should support named profile variants that retain the same conceptual contract but express different routing posture, for example:

- `codebrewRouter`
- `codebrewRouter-fast`
- `codebrewRouter-local-first`
- `codebrewRouter-private`

These variants should resolve against the same catalog and governance layer rather than becoming bespoke feature branches.

#### REQ-CBR-04 Explainability of routing

Blaze must be able to explain why `codebrewRouter` selected a given target. At minimum, the system should record:

- requested logical model
- resolved concrete target
- strategy or policy branch used
- fallback occurrence
- denial or override reason when policy changed the result

This must be available to operators and useful in debugging edge or mobile behavior.

#### REQ-CBR-05 Gateway and Edge parity

The same logical model contract must be available in both Blaze Gateway and Blaze Edge. A request written against `codebrewRouter` should not need a different model name or application-level branching when moved from connected mode to offline mode.

### Expanded Gateway Resilience and Health Requirements

#### REQ-RES-01 Pre-stream failover in the default gateway path

The default gateway route must support transparent failover before the first token is emitted. This is already implemented more robustly in `CodebrewRouterChatClient` than in the default `LlmRoutingChatClient`, so the detailed requirement is to bring the general gateway path up to the same standard.

#### REQ-RES-02 Failure categorization

Provider failures must be categorized into at least:

- auth or credential failure
- rate-limit or quota failure
- transient network failure
- timeout
- provider-unhealthy or unreachable
- unsupported-feature mismatch

Different categories should drive different routing or policy outcomes.

#### REQ-RES-03 Health-aware routing

Gateway routing must use asynchronous health state rather than discovering provider death only inside user requests. A background health monitor should be able to mark targets as degraded or unavailable so routing can avoid them proactively.

#### REQ-RES-04 Circuit breaking and recovery

The gateway must maintain per-target circuit-breaking behavior with configurable thresholds and cooldowns. Recovery should support lazy probing and periodic health refresh rather than requiring a full restart.

#### REQ-RES-05 Locality-aware graceful degradation

When a cloud target is denied or unavailable, Blaze should prefer a permitted local or local-server fallback when one exists. This is a core differentiator for the product and should be explicit in the requirements rather than left as an optimization.

### Expanded Governance, Authentication, and Spend Requirements

#### REQ-GOV-01 First-class client identity

Every request must be attributable to a client identity, whether through API key, bearer token, or a trusted internal identity. Anonymous access is incompatible with the desired product shape.

#### REQ-GOV-02 Virtual keys and policy-bound credentials

Blaze must support the concept of virtual keys or policy-bound credentials similar to LiteLLM-style gateway governance. A client credential should be able to express:

- allowed logical models
- allowed concrete providers
- cloud escalation rights
- quota and budget ceilings
- tenant or app ownership

#### REQ-GOV-03 Budgeting and usage controls

Blaze must support per-client controls for:

- request rate
- token usage
- spend estimation or cost-class budget
- locality restrictions such as cloud-forbidden or local-preferred

These are product requirements because they determine whether Blaze is an internal router or a platform other teams can safely consume.

#### REQ-GOV-04 Consistent enforcement across gateway and edge

Policy decisions that matter to safety or cost must not disappear in offline scenarios. Blaze Edge must honor the locally available subset of policy relevant to:

- model allowlists
- local-only or cloud-forbidden rules
- sync eligibility
- optional usage caps

### Expanded Blaze Edge and Mobile Requirements

#### REQ-EDGE-01 Edge is a product surface, not a hidden implementation detail

Blaze Edge must be specified as a supported runtime surface with its own contract, lifecycle, diagnostics, and packaging requirements. It should not be treated as a one-off local adapter bolted onto the server gateway.

#### REQ-EDGE-02 Runtime abstraction

Blaze Edge must abstract over at least two classes of local execution target:

- an embedded or in-process runtime path
- a local OpenAI-compatible server path such as LM Studio or llama.cpp style local serving

This is consistent with ADR-0005 and reduces product lock-in to a single local runtime technology.

#### REQ-EDGE-03 Compact offline model profile

Blaze Edge must support at least one compact offline profile that is suitable for disconnected execution on constrained hardware. As of 2026-04-24, the public Gemma line is Gemma 3 and Gemma 3n rather than a published "Gemma 4" line, so the requirement should remain phrased as a Gemma-family or equivalent compact profile until model selection is validated.

#### REQ-EDGE-04 Device readiness and artifact management

The edge runtime must define how a host determines:

- whether the local model profile is installed
- whether the artifact checksum and version are valid
- whether the model is warmed and ready
- whether the device has enough storage or compute headroom
- whether an update or manifest refresh is needed

#### REQ-EDGE-05 Mobile host integration

The product must define a supported integration story for mobile or field applications. At minimum, the requirements should support:

- host-side dependency injection or service registration
- stable request APIs for chat generation
- local diagnostics and logs
- reconnect-safe sync hooks

The exact first mobile host remains an open planning decision, but the integration contract should not wait for that decision.

#### REQ-EDGE-06 Resource-aware execution

Edge runtime requirements must account for:

- storage budget
- battery impact
- CPU, GPU, or NPU availability
- cold-start behavior
- model download size and resumability

These constraints are central to mobile feasibility and should drive the eventual supported-device matrix.

### Expanded Sync and Data-Policy Requirements

#### REQ-SYNC-01 Manifest and policy sync

Blaze Edge must be able to receive updated policy, catalog, and model-profile metadata from Blaze Gateway when online. This is how the logical model contract remains coherent across connected and offline modes.

#### REQ-SYNC-02 Usage and operational sync

Edge should be able to sync non-sensitive operational metadata such as:

- usage records
- routing outcomes
- model version state
- health or readiness snapshots

This should be configurable so deployments can choose local-only mode if required.

#### REQ-SYNC-03 Privacy-aware content sync

Prompt and conversation content must not be uploaded from edge to gateway by default unless policy explicitly allows it. The product requirement should distinguish:

- operational sync
- summary-only sync
- full-content sync

#### REQ-SYNC-04 Optional data-plane extensions

Repo planning notes include offline RAG and local document storage ideas. These should be treated as optional data-plane extensions rather than core phase-1 gateway requirements. The PRD should reserve the extension point without making RAG a prerequisite for the base platform.

### Expanded Tooling and MCP Requirements

#### REQ-TOOL-01 MCP remains a strategic extension path

The repo already contains MCP connection and delegating-client scaffolding. The product requirement is to preserve MCP as a supported extension path, while being explicit that MCP is not currently live in the default runtime.

#### REQ-TOOL-02 Tool policy and locality awareness

Tools must eventually be governed by:

- client entitlement
- target runtime capability
- locality and privacy rules
- offline availability

This matters because some tools will only make sense when connected, while others may be safe for edge-local execution.

### Expanded Observability and Admin Requirements

#### REQ-OBS-01 Structured routing ledger

Every request should leave behind a structured routing ledger containing at least:

- logical model requested
- resolved target
- route strategy or profile
- fallback chain traversal
- health state used
- latency and usage
- locality of execution

#### REQ-OBS-02 Cost and token accounting

Blaze must estimate or measure token usage and cost per request, even if some providers do not return full usage objects. The product should define how estimated usage is distinguished from authoritative usage.

#### REQ-OBS-03 Admin surfaces

The admin experience should eventually expose:

- provider and model catalog
- health and degradation state
- key and policy management
- budget and usage views
- edge fleet visibility where applicable

This can be implemented via APIs first and UI second, but the requirement is product-level.

### Expanded Testing and Quality Gates

#### REQ-TEST-01 Tier-A gateway integration tests

Fast integration tests against the Api host must cover:

- chat streaming contract
- non-streaming contract
- invalid request handling
- tool passthrough behavior
- multimodal request parsing
- logical model resolution

#### REQ-TEST-02 Tier-B distributed integration tests

Aspire-orchestrated tests must cover scenarios where multiple resources matter together, including:

- provider startup and service discovery
- health endpoints
- model discovery across configured targets
- any future dev or admin surfaces that depend on AppHost wiring

#### REQ-TEST-03 Config-safety tests

The test suite must catch the class of bug where routing or profile configuration references a provider that is not actually registered. This is a direct lesson from the current `GithubModels` mismatch in repo planning materials.

#### REQ-TEST-04 Edge scenario tests

Before Blaze Edge is considered viable, scenario coverage must include:

- offline request success
- reconnect and manifest refresh
- allowed local-only policy
- denied cloud escalation
- local model missing or not ready

#### REQ-TEST-05 Benchmark and overhead visibility

The benchmarks project must eventually validate routing overhead, pre-stream failover cost, and representative local-versus-cloud execution latency so product decisions are backed by measurement rather than intuition.

### Research-Informed Adjustments

The following requirement adjustments are informed by current external documentation as of 2026-04-24:

- LiteLLM's strongest gateway differentiators are not just provider count. They are virtual keys, auth hooks, cost tracking, budgets, and rate limiting. Blaze requirements should therefore prioritize governance early rather than treating it as polish.
- LM Studio documents an OpenAI-compatible local server surface with `GET /v1/models`, `POST /v1/chat/completions`, `POST /v1/completions`, and related endpoints. This reinforces the repo ADR direction of treating local runtimes as catalog entries rather than bespoke one-off adapters.
- Current public Google documentation and model cards reference Gemma 3 and Gemma 3n as the portable Gemma-family options. The PRD should therefore avoid locking Blaze Edge to a literal `Gemma 4` requirement until that target is concretely validated.

## Non-Functional Requirements

### NFR-01 Compatibility

- Blaze must remain compatible with mainstream OpenAI-style client integrations for the supported endpoint set.
- Gateway and edge runtimes must preserve the `codebrewRouter` logical model contract consistently.

### NFR-02 Performance

- Gateway routing overhead should remain negligible relative to generation latency.
- Edge local inference must target acceptable response time for compact models on supported device classes.
- Cold-start, warm-start, and model-download behavior must be explicit and measurable.

### NFR-03 Reliability

- If any approved target is available, Blaze should prefer graceful degradation over hard failure.
- Edge offline workflows must continue without internet access once the local model profile is installed.
- Sync failures must not corrupt local conversation state.

### NFR-04 Security and Privacy

- Provider credentials and policy secrets must never be stored in source control.
- Sync behavior must be privacy-aware and configurable.
- Local model artifacts, prompts, and usage records must follow explicit retention rules.
- Policy must support local-only and no-cloud modes where required.

### NFR-05 Maintainability

- Adding a new provider or local runtime target should be a catalog and adapter exercise, not a broad rewrite.
- Routing, policy, and provider adapters must remain testable in isolation.
- The architecture should preserve clean boundaries among API contract, policy layer, provider adapters, and edge runtime.

### NFR-06 Testability

- Blaze must maintain a high automated test bar for routing, compatibility, and failure handling.
- Gateway and edge behavior must be covered by unit, integration, and scenario tests.
- Offline and reconnect flows must be testable with deterministic harnesses.

## Release Strategy

### Phase 1: Gateway Hardening

Objective:
Make the current repository production-ready as a connected LLM gateway.

Key outcomes:

- tighten OpenAI compatibility
- complete typed DTO coverage
- improve provider abstraction and model catalog
- add auth, key policy, budgets, and observability
- complete routing resilience and failover basics

### Phase 2: `codebrewRouter` Policy Model

Objective:
Elevate `codebrewRouter` from a useful concept to the primary contract applications rely on.

Key outcomes:

- policy-based virtual model resolution
- named routing profiles
- routing explainability
- richer capability metadata and selection rules

### Phase 3: Blaze Edge Foundation

Objective:
Introduce an embeddable local runtime for offline and mobile use cases.

Key outcomes:

- local model profile support
- offline inference path
- local session continuity
- health and readiness reporting

### Phase 4: Connected and Offline Parity

Objective:
Make connected and offline behavior feel like one product instead of two separate integrations.

Key outcomes:

- sync and reconciliation
- shared logical model contract
- admin visibility into edge state
- mature policy enforcement across environments

## Acceptance Criteria by Product Level

### Platform Baseline Acceptance

- A Blaze client can target `codebrewRouter` without needing provider-specific request logic.
- The gateway can resolve `codebrewRouter` to an approved target and return an OpenAI-compatible response.
- Routing decisions can be observed and explained.
- Operators can restrict and meter model usage by client.

### Offline Acceptance

- An offline-capable host can execute a request against `codebrewRouter` without network connectivity after the local model profile is installed.
- The offline experience uses the same logical model naming scheme as the online experience.
- Edge can reconnect and sync non-sensitive operational data without breaking local history.

### Planning Readiness Acceptance

- The product is decomposed into gateway, policy, and edge workstreams that can be planned independently.
- Each workstream has clear responsibilities and integration boundaries.
- Open questions are explicit enough for follow-on requirements drafting.

## Success Metrics

The following targets should be refined during phase planning, but they represent the desired operating shape:

- 90% or more of supported client scenarios integrate without provider-specific code changes
- 95% or more of routable requests succeed when at least one approved execution target is healthy
- routing overhead stays below an agreed p95 threshold that is small relative to model latency
- offline-capable apps can complete their core generation flow without cloud access after initial model provisioning
- operator dashboards can account for request volume, routing distribution, and usage across cloud and local targets

## Dependencies

- `Microsoft.Extensions.AI` and compatible provider adapters
- stable provider integrations for Azure Foundry, local runtimes, and any approved external providers
- a chosen on-device inference technology for Blaze Edge
- packaging and licensing decisions for compact offline model profiles
- secure key and policy distribution mechanisms
- Aspire and current .NET hosting infrastructure for the connected gateway

## Risks

### On-Device Runtime Feasibility

Mobile offline inference may be limited by device memory, packaging constraints, or runtime support.

### Model Packaging and Licensing

A compact model profile may be technically suitable but operationally blocked by redistribution or licensing terms.

### Compatibility Drift

OpenAI and LiteLLM client expectations can shift over time, increasing maintenance pressure on the compatibility layer.

### Policy Complexity

If routing policy becomes too opaque, `codebrewRouter` may be difficult for developers and operators to trust.

### Sync and Privacy Tension

Operators may want rich edge telemetry while customers may require strict local-only behavior.

## Open Questions

1. What is the first supported Blaze Edge host: .NET MAUI, Android-native, iOS-native, desktop, or a thin local service model?
2. Which on-device inference runtime should Blaze Edge standardize on first?
3. What exact endpoint set defines Blaze's supported LiteLLM-compatible profile in v1?
4. Should `codebrewRouter` remain one general profile plus named variants, or become a broader family of virtual models?
5. What data is allowed to sync from edge to gateway by default?
6. How much offline tooling support is required in v1 versus online-only tool support?
7. What minimum device class should be considered supported for the compact offline model profile?

## Recommended Next Planning Docs

This PRD is the system-level requirements anchor. The next documents should likely be:

1. a gateway hardening requirements doc
2. a `codebrewRouter` policy and model-catalog design doc
3. a Blaze Edge offline and mobile architecture doc
4. a governance and metering requirements doc

## Source Context

This PRD was drafted from the current repository state and the planning documents already in the repo, including:

- `C:\src\CodebrewRouter\Docs\PRD\blaze-llmgateway-prd.md`
- `C:\src\CodebrewRouter\Docs\PRD\litellm-compatible-gateway.md`
- `C:\src\CodebrewRouter\Docs\design\adr\0005-local-runtime-compatibility.md`
- `C:\src\CodebrewRouter\Docs\summary\summary.md`
- `C:\src\CodebrewRouter\Docs\codex\2026-04-24-blaze-current-state-assessment.md`
