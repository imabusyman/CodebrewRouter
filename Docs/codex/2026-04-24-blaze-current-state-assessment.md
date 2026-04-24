# Blaze Current-State Assessment

Date: 2026-04-24

## Executive Summary

Blaze.LlmGateway already has a credible routing-gateway core, but it is not yet a LiteLLM-class platform.

The strongest parts of the current codebase are:

- A clean `Microsoft.Extensions.AI` middleware shape using `DelegatingChatClient`
- A working OpenAI-style API surface for chat completions, legacy completions, and model listing
- Clear direction in ADRs and planning docs for local-first, LAN-aware, and agent-capable evolution
- A substantial test suite around routing and endpoint behavior

The biggest current limitations are:

- Runtime implementation is behind the design docs and ADRs
- Provider modeling is still enum-centric instead of catalog/capability driven
- MCP is scaffolded but disabled
- Streaming failover is incomplete
- OpenAI compatibility is partial rather than strict
- Text-only DTOs currently limit multimodal and offline/mobile ambitions

## What Is Actually Implemented Today

### API Surface

The API host in `Blaze.LlmGateway.Api` exposes:

- `POST /v1/chat/completions`
- `POST /v1/completions`
- `GET /v1/models`
- OpenAPI at `/openapi/v1.json`
- Scalar docs at `/scalar`

Key files:

- `Blaze.LlmGateway.Api/Program.cs`
- `Blaze.LlmGateway.Api/ProgramPartial.cs`
- `Blaze.LlmGateway.Api/ChatCompletionsEndpoint.cs`
- `Blaze.LlmGateway.Api/CompletionsEndpoint.cs`
- `Blaze.LlmGateway.Api/ModelsEndpoint.cs`
- `Blaze.LlmGateway.Api/OpenAiModels.cs`

### Routing Core

The infrastructure layer is the real asset in the repo right now.

Key implemented pieces:

- `LlmRoutingChatClient` routes requests to keyed providers
- `ConfiguredFailoverStrategy` provides configured fallback chains
- `OllamaMetaRoutingStrategy` performs semantic routing using a local Ollama router model
- `KeywordRoutingStrategy` provides a fallback heuristic
- `CodebrewRouterChatClient` introduces a virtual model concept with task-based fallback rules

Key files:

- `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`
- `Blaze.LlmGateway.Infrastructure/LlmRoutingChatClient.cs`
- `Blaze.LlmGateway.Infrastructure/CodebrewRouterChatClient.cs`
- `Blaze.LlmGateway.Infrastructure/RoutingStrategies/OllamaMetaRoutingStrategy.cs`
- `Blaze.LlmGateway.Infrastructure/RoutingStrategies/ConfiguredFailoverStrategy.cs`

### Provider Registration

The current configured provider model is still small and static. `LlmGatewayOptions` currently defines:

- `AzureFoundry`
- `FoundryLocal`
- `OllamaLocal`

This is implemented in:

- `Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs`

This means the codebase is not yet at the provider/model catalog shape described by the design docs.

### Model Catalog

The repo has an early model-catalog direction:

- Azure Foundry models can be discovered dynamically
- Foundry Local is surfaced from config
- `codebrewRouter` can appear as a virtual model

Key files:

- `Blaze.LlmGateway.Api/ModelCatalogService.cs`
- `Blaze.LlmGateway.Api/AzureFoundryModelDiscovery.cs`
- `Blaze.LlmGateway.Core/ModelCatalog/*`

## What Is Strong

### 1. The middleware architecture is good

This is the most promising part of Blaze. The repo is already using MEAI in a way that will scale:

- provider clients as keyed `IChatClient`
- routing and policy in middleware
- future resilience and tool layers as additional delegating clients

That is a cleaner long-term model than a pile of provider-specific conditionals in the API layer.

### 2. The virtual model idea is valuable

`codebrewRouter` is an important concept. It should become the stable user-facing model identity while Blaze chooses the actual backend based on:

- task type
- availability
- locality
- policy
- online/offline status

That concept maps very well to both server routing and future edge/mobile execution.

### 3. The docs show a coherent north-star

The repo already contains a thoughtful future direction in:

- `Docs/plan/llm-agent-platform-plan.md`
- `Docs/design/tech-design/blaze-llmgateway-architecture.md`
- `Docs/design/adr/0005-local-runtime-compatibility.md`
- `Docs/EXPANSION_PLAN_2026.md`

Those docs already align with:

- LM Studio and llama.cpp as OpenAI-compatible local runtimes
- Gemma-family local models as catalog entries
- local-first and cloud-escalation policy
- a future offline SDK / edge runtime

## What Is Missing or Incomplete

### 1. Docs are ahead of code

The design docs describe a more advanced system than the runtime currently delivers. In particular, the following are still mostly planned rather than fully implemented:

- provider descriptor / model profile catalog
- circuit-breaker layer
- hardened MCP tool hosting
- durable session persistence
- agent integration layer
- cloud-escalation enforcement

### 2. MCP exists in code but is disabled in composition

There is real scaffolding for:

- `McpConnectionManager`
- `McpToolDelegatingClient`

But the composition path in `Program.cs` and `InfrastructureServiceExtensions.cs` keeps MCP disabled right now. So MCP is not part of the live runtime.

### 3. Provider identity is still too rigid

The current provider identity is still based on `RouteDestination`:

- `AzureFoundry`
- `FoundryLocal`
- `GithubModels`

That is too limited for the direction you described. It will not scale well to:

- multiple local runtimes
- multiple models per runtime
- Gemma variants
- mobile/offline runtimes
- policy-based selection by capability or locality

### 4. The wire contract still needs hardening

The OpenAI-compatible API exists, but the implementation is still closer to an early compatibility layer than a strict compatibility surface.

Important gaps include:

- tool/function definitions are present in DTOs but are not fully flowed into runtime behavior
- multimodal content is not modeled richly enough for vision/mobile scenarios
- streaming behavior and fallback behavior still need hardening

### 5. LiteLLM-class platform features are not in place yet

Blaze is not yet a management-grade platform. Still missing:

- API keys and virtual keys
- model access policy per client
- spend and usage tracking
- rate limiting
- multi-tenant controls
- operational admin surface

## Testing and Validation Snapshot

Observed during this review:

- `dotnet test Blaze.LlmGateway.Tests\\Blaze.LlmGateway.Tests.csproj --no-build` passed
- Results: 127 passed, 1 skipped

Important caveat:

- a fresh `dotnet build` from this session was inconclusive because the sandboxed environment hit first-time-use / restore behavior before actual compilation evidence was established

So the test suite is a positive signal, but this note does not certify a clean full build from scratch.

## Assessment Against Your LiteLLM Direction

If the question is "Can Blaze become a LiteLLM-style .NET platform?" the answer is yes.

If the question is "Is it there now?" the answer is no.

Right now Blaze is best understood as:

- a routing engine core
- an OpenAI-compatible API shell
- a good architectural seed for a larger platform

It is not yet:

- a full provider abstraction platform
- a governance layer
- a mobile/offline-ready runtime
- a complete LiteLLM equivalent

## Assessment Against Your Mobile / Offline Gemma Direction

Your idea of using Blaze on mobile or in offline environments through a simple local model like Gemma is directionally correct, and the repo already hints at this future.

The right architectural interpretation is:

- Blaze Gateway: the server-hosted router and policy plane
- Blaze Edge: an offline/local runtime that exposes the same logical routing contract

In that model:

- clients target a stable virtual model like `codebrewRouter`
- server-hosted Blaze can route to cloud, LAN, or local runtimes
- edge/mobile Blaze can route only among local/on-device runtimes
- Gemma-family models become selectable local backends, not special-case code paths

This is better than treating "mobile" as just another provider in the current server host.

## Recommended Requirement Direction

### Near-Term Requirements

Before pushing deeper into agent/runtime expansion, the next requirements should focus on hardening the gateway:

1. Strict OpenAI compatibility for request and response contracts
2. Rich content-part support for multimodal requests
3. Tool/function execution passthrough
4. Provider/model catalog replacing enum-based routing identity
5. Real resilience layers: health, circuit breaking, pre-stream failover
6. Auth, policy, and usage controls

### Medium-Term Requirements

After hardening the gateway core:

1. Introduce a provider descriptor and model profile system
2. Add local-runtime entries for LM Studio, llama.cpp, Ollama, and Foundry Local
3. Model Gemma-family variants as catalog entries with capability metadata
4. Add locality-aware routing: device, LAN, cloud
5. Add a lightweight admin/control plane

### Offline / Mobile Requirements

For the offline/mobile path, requirements should likely define a separate edge package or SDK:

1. `OfflineLlmGateway` or `Blaze Edge` shared library
2. Local model manifest and registry
3. On-device routing strategy
4. Local health monitoring
5. Optional local SQLite-backed RAG store
6. Sync path back to the main Blaze gateway when online
7. Same MEAI-facing contract as the server host

This lets external apps reuse the Blaze routing abstraction without needing the entire server runtime.

## Bottom-Line Summary

Blaze today is a promising gateway core with strong architectural instincts, especially around MEAI middleware and virtual-model routing.

The most important conclusion is that the foundation is good enough to justify continued planning, but the next requirements should be written around hardening and cataloging the current gateway before expanding into a full LiteLLM alternative or a mobile/offline product.

For your Gemma/mobile/offline vision specifically, the best next architectural move is to define Blaze as a two-part system:

- a server gateway
- an edge/offline runtime

with `codebrewRouter` acting as the stable user-facing logical model across both.
