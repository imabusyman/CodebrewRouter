# A2A Protocol and `a2aproject/a2a-dotnet` Research

Repository context: [a2aproject/a2a-dotnet](https://github.com/a2aproject/a2a-dotnet) and the protocol source repo behind `https://a2a-protocol.org/latest/` (`a2aproject/A2A`).

## Executive Summary

A2A is an **agent-to-agent interoperability protocol**, not a tool-calling protocol. Its core abstractions are **Agent Cards** for discovery, **Messages** for conversational turns, **Tasks** for stateful work, **Parts** for multimodal payloads, **Artifacts** for deliverables, plus streaming and push-notification mechanics for long-running execution.[^1][^2][^3] The protocol is explicitly positioned as complementary to MCP: MCP connects agents/models to tools and resources, while A2A lets autonomous agents collaborate as peers across organizational and framework boundaries.[^1][^4]

The `.NET` SDK is already a real protocol implementation, not just model classes. It ships:

- a JSON-RPC client (`A2AClient`)
- an HTTP+JSON client (`A2AHttpJsonClient`)
- discovery support (`A2ACardResolver`)
- a server runtime (`A2AServer`, `IAgentHandler`, `ITaskStore`, `TaskUpdater`)
- ASP.NET Core route mapping for JSON-RPC and REST-style bindings.[^5][^6][^7][^8][^9]

For CodebrewRouter, the important conclusion is that **adding A2A is not equivalent to adding another chat endpoint**. To support A2A faithfully, you would be adding a second protocol surface with discovery, task lifecycle state, SSE streams, and optional push-notification configuration alongside the existing OpenAI-compatible API.[^2][^3][^8]

## What the Protocol Actually Requires

The protocol docs define A2A around three layers: canonical data model, abstract operations, and concrete protocol bindings.[^2] The normative source is the proto file, and the spec says SDKs and schemas should be regenerated from that proto rather than edited by hand.[^2][^10]

The key protocol operations are broader than chat completion:

1. `SendMessage`
2. `SendStreamingMessage`
3. `GetTask`
4. `ListTasks`
5. `CancelTask`
6. `SubscribeToTask`
7. push-notification config CRUD
8. `GetExtendedAgentCard`[^2][^10][^11]

At the wire level, the proto maps these to concrete HTTP routes like `/message:send`, `/message:stream`, `/tasks/{id}`, `/tasks/{id}:cancel`, `/tasks/{id}:subscribe`, `/tasks/{task_id}/pushNotificationConfigs`, and `/extendedAgentCard`, with optional tenant-prefixed variants.[^10] The protocol also requires SSE semantics for streaming task updates and supports webhook-style push notifications for disconnected or very long-running clients.[^3][^10]

That means a faithful implementation is a **task-oriented remote-agent API**, not just a prompt-in / token-stream-out facade.[^1][^2][^3]

## How `a2a-dotnet` Is Organized

The SDK README describes two main packages: `A2A` for the protocol core and `A2A.AspNetCore` for hosting integration. It targets A2A `v1.0`, supports both JSON-RPC and HTTP+JSON bindings, and includes SSE streaming. The core package multi-targets `net10.0` and `net8.0`.[^5][^12]

The main architectural split looks like this:

| Area | What it provides |
|---|---|
| `Client` | protocol clients, discovery, binding-specific transport |
| `Models` | protocol DTOs mirroring the A2A data model |
| `Server` | task lifecycle orchestration, persistence abstraction, event emission |
| `JsonRpc` | method-name constants and request/response plumbing |
| `A2A.AspNetCore` | endpoint mapping, request processors, SSE result types |
| `samples` | reference agents, CLI, Semantic Kernel integration |
| `tests` | serialization, request validation, HTTP/JSON-RPC processor behavior |[^5][^6][^7][^8][^9][^13][^14][^15]

## Client-Side SDK Design

The SDK exposes two first-class clients:

- `A2AClient` for JSON-RPC over HTTP
- `A2AHttpJsonClient` for the HTTP+JSON/REST binding[^6][^7]

`A2AClient` wraps JSON-RPC method calls for message sending, streaming, task operations, subscriptions, push-notification config, and extended card retrieval. For streaming it sets `Accept: text/event-stream`, reads SSE frames, deserializes each frame into a JSON-RPC response, and yields typed `StreamResponse` objects.[^6]

`A2AHttpJsonClient` does the same for REST-style routes such as `/message:send`, `/message:stream`, `/tasks/{id}`, `/tasks/{id}:cancel`, and push-notification config endpoints.[^7] This matters because it shows the SDK treats **both bindings as first-class**, not as an afterthought.[^6][^7]

For discovery, `A2ACardResolver` fetches `/.well-known/agent-card.json` by default and deserializes the server's `AgentCard` so clients can choose the right interface, auth flow, and capabilities before sending work.[^8]

## Server-Side SDK Design

The server runtime is where the repo becomes especially useful for your project.

`IAgentHandler` is intentionally narrow: you implement `ExecuteAsync(RequestContext, AgentEventQueue, CancellationToken)`, and optionally override `CancelAsync`. The SDK handles the heavier mechanics around the handler.[^9]

`A2AServer` is the actual orchestration layer. Its own class comment says it handles request lifecycle, context resolution, task persistence, history management, terminal-state guards, cancel support, and observability.[^16] In practice it:

- resolves or creates task/context state
- appends history for continuations
- supports `return_immediately`
- runs the handler against an event queue
- persists emitted task/message/artifact events
- decouples background execution from client connection lifetime for streaming and cancellation.[^16]

`TaskUpdater` is the ergonomic helper for handler authors. It exposes explicit lifecycle methods such as `SubmitAsync`, `StartWorkAsync`, `AddArtifactAsync`, `CompleteAsync`, `FailAsync`, `CancelAsync`, `RejectAsync`, `RequireInputAsync`, and `RequireAuthAsync`.[^17] That maps very directly to the protocol's task-state model and is one of the cleanest parts of the SDK.

Persistence is abstracted behind `ITaskStore`, which only needs `GetTaskAsync`, `SaveTaskAsync`, `DeleteTaskAsync`, and `ListTasksAsync`.[^18] The included `InMemoryTaskStore` shows the expected behavior: filter by context/status/timestamp, sort by latest status timestamp, paginate with tokens, trim history according to request, and omit artifacts unless requested.[^19] That gives you a strong template for replacing it with durable storage later.

## Protocol Models and Wire Shape

The `.NET` models line up closely with the protocol docs and proto:

- `AgentCard` carries identity, interfaces, capabilities, skills, default modes, and security declarations.[^20]
- `AgentTask` holds `id`, `contextId`, `status`, `history`, `artifacts`, and metadata.[^21]
- `Part` is the modality container: `text`, `raw`, `url`, or `data`, plus `mediaType`, `filename`, and metadata.[^22][^10]
- `SendMessageResponse` is a field-presence union of `Task` or `Message`.[^23]
- `StreamResponse` is a field-presence union of `Task`, `Message`, `StatusUpdate`, or `ArtifactUpdate`.[^24]
- `TaskStatusUpdateEvent` and `TaskArtifactUpdateEvent` model the incremental stream payloads.[^25][^26]

This is important for implementation planning: A2A is already **state-and-artifact aware**, so if you add it to CodebrewRouter you would need a task model and persistence story rather than only request/response chat transcripts.[^2][^3][^10][^21]

## ASP.NET Core Hosting and Binding Support

`A2A.AspNetCore` exposes endpoint mapping for both JSON-RPC and REST-style bindings.

`MapA2A(...)` maps the JSON-RPC endpoint, while `MapWellKnownAgentCard(...)` serves the discovery card at `/.well-known/agent-card.json`.[^11] `MapHttpA2A(...)` maps REST endpoints for card access, tasks, message send/stream, subscriptions, push-notification config, and extended agent card retrieval.[^11]

Two implementation details matter a lot:

1. The route builder explicitly says the REST binding currently **does not support the spec's multi-tenant route variants**, even though the protocol proto defines tenant-prefixed HTTP bindings.[^10][^11]
2. The SDK adds a convenience `/card` endpoint for REST access, and the route builder notes that this is **not part of the A2A spec**.[^11]

The JSON-RPC processor is also practical rather than toy-level. It:

- accepts `A2A-Version` `1.0` and `0.3`
- branches between single-response and streaming methods
- validates empty `parts`
- special-cases push-notification method support
- returns proper JSON-RPC errors for invalid params and method lookup failures.[^27]

The HTTP processor maps protocol exceptions to HTTP status codes and emits SSE frames in `data: <json>\n\n` format for REST streams.[^28]

## One Important Source-of-Truth Warning

The repo contains `src/A2A/openapi.yaml`, but its own first line says it is **"Experimental thoughts"** for what an HTTP API might look like.[^29] It uses older or different paths like `/tasks/{id}/cancel`, `/tasks/{id}/send`, `/tasks/{id}/sendSubscribe`, `/tasks/{id}/resubscribe`, and `/tasks/{id}/pushNotification`.[^29]

That file is **not** the right source of truth for current A2A `v1.0` behavior. The better sources are:

- the protocol repo's spec and proto[^2][^10]
- the SDK's actual HTTP client routes[^7]
- the ASP.NET route builder and processors.[^11][^28]

So if you recreate endpoints in your own project, follow the protocol repo and actual SDK behavior, not `openapi.yaml` in `a2a-dotnet`.[^7][^10][^11][^29]

## Samples and What They Demonstrate

The sample server shows the intended hosting model: register an agent implementation, optionally swap task stores, map the A2A endpoint, and publish the well-known agent card.[^13] That is the shape I would treat as the reference for exposing CodebrewRouter as an A2A server.

The Semantic Kernel sample is especially informative because it shows how the protocol layer stays separate from the internal agent runtime. `SemanticKernelTravelAgent` implements `IAgentHandler`, uses `TaskUpdater` for lifecycle events, then delegates the actual work to a `ChatCompletionAgent` built with Semantic Kernel and either OpenAI or Azure OpenAI providers.[^14] For your project, the analogous pattern would be: **keep A2A as the outer protocol layer and plug your existing MEAI-based routing pipeline in behind `IAgentHandler`** rather than trying to reshape A2A into OpenAI chat completions.

## Test Coverage and Practical Confidence

The tests give confidence that the SDK is implementing protocol details intentionally:

- JSON-RPC processor tests cover ID validation, invalid method handling, invalid params handling, and end-to-end message-send behavior producing a submitted task with preserved history.[^15]
- HTTP processor tests verify exception-to-status-code mapping across task-not-found, invalid params, unsupported operation, content-type errors, and internal errors.[^30]
- serialization tests confirm round-trip behavior for requests, events, stream responses, and v1 enum serialization like `TASK_STATE_COMPLETED` and `ROLE_USER`.[^31]

That is useful because it shows the project is testing protocol semantics, not just happy-path sample code.

## What This Means for CodebrewRouter

If your goal is to "recreate these endpoints" in CodebrewRouter, I would interpret that as **adding an optional A2A-facing API surface**, not retrofitting the current `/v1/chat/completions` route.

My recommended architecture would be:

1. Keep the existing OpenAI-compatible API exactly as its own surface.
2. Add a separate A2A module/endpoints area with:
   - `/.well-known/agent-card.json`
   - JSON-RPC `SendMessage` / `SendStreamingMessage`
   - task retrieval/list/cancel/subscribe
   - optional push-notification config only if you are ready to support outbound webhook security properly.[^3][^10][^11]
3. Represent CodebrewRouter itself as an **A2A server** whose `IAgentHandler` delegates to your existing model-routing pipeline.
4. Start with JSON-RPC plus the well-known agent card first; add REST binding only if you need it.
5. Do not copy `a2a-dotnet`'s experimental `openapi.yaml` surface; follow the protocol repo and actual SDK implementations instead.[^7][^10][^11][^29]

The reason for keeping the surfaces separate is straightforward: A2A introduces discovery, task IDs, context IDs, state transitions, artifacts, and multi-operation lifecycle semantics that do not fit naturally inside a single OpenAI-style chat-completions contract.[^2][^3][^10][^21]

## Confidence Assessment

**High confidence**

- A2A is intended for agent-to-agent collaboration and is complementary to MCP rather than a replacement for it.[^1][^4]
- The protocol requires more than chat endpoints: it includes discovery, task lifecycle operations, streaming, and optional push notifications.[^2][^3][^10]
- `a2a-dotnet` already implements both JSON-RPC and HTTP+JSON bindings with real client, server, ASP.NET, and test support.[^5][^6][^7][^11][^15][^30][^31]

**Moderate confidence / design inference**

- My recommendation to expose A2A as a separate API surface in CodebrewRouter is an architectural conclusion based on the protocol shape and the current gateway design, not a statement made explicitly by the A2A docs.[^2][^3][^10][^11]
- My recommendation to begin with JSON-RPC plus discovery before REST is a pragmatic sequencing suggestion based on the SDK samples and the route-builder caveat about tenant support, not a protocol requirement.[^11][^13]

## Footnotes

[^1]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a\repo\docs\topics\what-is-a2a.md:3-9,94-153,172-214`
[^2]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a\repo\docs\specification.md:13-39,92-123,131-145,159-258`
[^3]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a\repo\docs\topics\streaming-and-async.md:3-25,43-57,77-111`
[^4]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a\repo\docs\topics\a2a-and-mcp.md:10-35,39-67,120-132`
[^5]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\README.md:10-21,45-67,159-187`; `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\A2A.csproj:4-18,21-43`
[^6]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Client\A2AClient.cs:31-100,113-150,166-232`
[^7]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Client\A2AHttpJsonClient.cs:12-24,33-149,158-237`
[^8]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Client\A2ACardResolver.cs:21-45,53-89`
[^9]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Server\IAgentHandler.cs:4-32`
[^10]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a\repo\specification\a2a.proto:18-139,142-184,186-242`
[^11]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A.AspNetCore\A2AEndpointRouteBuilderExtensions.cs:17-35,56-68,77-147`
[^12]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\README.md:18-26`; `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\A2A.csproj:3-18`
[^13]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\samples\AgentServer\Program.cs:21-56,89-105`
[^14]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\samples\SemanticKernelAgent\SemanticKernelTravelAgent.cs:101-151,153-193,204-275`
[^15]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\tests\A2A.AspNetCore.UnitTests\A2AJsonRpcProcessorTests.cs:61-91,95-144,147-191,230-268`
[^16]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Server\A2AServer.cs:10-16,23-27,75-99,149-171,195-239,267-360`
[^17]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Server\TaskUpdater.cs:20-32,37-49,58-79,84-191`
[^18]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Server\ITaskStore.cs:8-13,15-47`
[^19]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Server\InMemoryTaskStore.cs:41-118`
[^20]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\AgentCard.cs:5-56`
[^21]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\AgentTask.cs:6-29`
[^22]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\Part.cs:21-79`; `C:\src\CodebrewRouter\research\sources\a2aproject-a2a\repo\docs\topics\key-concepts.md:63-89`
[^23]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\SendMessageResponse.cs:16-30`
[^24]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\StreamResponse.cs:20-42`
[^25]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\TaskStatusUpdateEvent.cs:6-22`
[^26]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\Models\TaskArtifactUpdateEvent.cs:6-28`
[^27]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A.AspNetCore\A2AJsonRpcProcessor.cs:13-24,44-49,79-97,99-169,196-220`
[^28]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A.AspNetCore\A2AHttpProcessor.cs:106-127,131-173,185-273,297-343`
[^29]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\src\A2A\openapi.yaml:1-17,40-49,50-157`
[^30]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\tests\A2A.AspNetCore.UnitTests\A2AHttpProcessorTests.cs:123-180`
[^31]: `C:\src\CodebrewRouter\research\sources\a2aproject-a2a-dotnet\repo\tests\A2A.UnitTests\ParsingTests.cs:67-92,95-150,152-200`
