# ADR-0003: Northbound API surface — OpenAI Chat Completions (typed, hardened) in Phase 1

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture, Product
- **Related:** ADR-0002, ADR-0007, ADR-0008, [PRD §5 FR-01, §8.1](../../PRD/blaze-llmgateway-prd.md), [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Phase 1 - Gateway hardening", §"Phase 4 - Client ecosystem integration"

## Context

Today the northbound surface is a single minimal-API endpoint in [Blaze.LlmGateway.Api/Program.cs](../../Blaze.LlmGateway.Api/Program.cs):

```csharp
app.MapPost("/v1/chat/completions", async (HttpRequest request, IChatClient chatClient) =>
{
    using var doc = await JsonSerializer.DeserializeAsync<JsonDocument>(request.Body);
    // manual JsonDocument walking to extract messages[].role, messages[].content
    // writes an SSE-ish shape but does not match OpenAI spec
});
```

Gaps that block real BYOM clients:

- Request body is parsed ad-hoc. `tool_calls`, `tool_choice`, `temperature`, `max_tokens`, `stream`, `stop`, `response_format`, `seed`, `n` are silently dropped.
- Response does not emit the `id`, `object`, `created`, `model`, `finish_reason`, `usage`, or final `chunk` with `finish_reason: stop` — Copilot CLI, Claude Code, Codex, and OpenAI SDK clients will misbehave.
- No error contract. An exception yields a 500 with a plain ASP.NET error page — no `{ "error": { "type", "code", "message" } }` body.
- No OpenAPI/Swagger description.

Meanwhile the north-star plan mentions ambitions for Responses API (OpenAI's session-oriented surface), A2A (Agent-to-Agent), and MCP export. Shipping all of those in Phase 1 would bloat the design and delay the hardening work.

## Decision

We will **ship a single, specification-accurate OpenAI Chat Completions API (`POST /v1/chat/completions`) in Phase 1, backed by typed DTOs and an explicit error contract**. Other northbound surfaces (Responses, A2A, MCP export, durable-session API) are **explicitly deferred to Phase 3 or later** and tracked as follow-on ADRs.

### Details

**DTOs.** Land under `Blaze.LlmGateway.Integrations/OpenAI/`:

```csharp
// Requests
public sealed record ChatCompletionRequest(
    string Model,
    IReadOnlyList<ChatCompletionMessage> Messages,
    bool? Stream = false,
    double? Temperature = null,
    int? MaxTokens = null,
    double? TopP = null,
    IReadOnlyList<string>? Stop = null,
    int? N = null,
    double? PresencePenalty = null,
    double? FrequencyPenalty = null,
    int? Seed = null,
    ResponseFormat? ResponseFormat = null,
    IReadOnlyList<ChatCompletionTool>? Tools = null,
    ToolChoice? ToolChoice = null,
    IReadOnlyDictionary<string, object>? Metadata = null);

public sealed record ChatCompletionMessage(
    string Role,                                   // "system" | "user" | "assistant" | "tool"
    string? Content = null,
    string? Name = null,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null);

// Non-streaming response
public sealed record ChatCompletionResponse(
    string Id, string Object, long Created, string Model,
    IReadOnlyList<ChatCompletionChoice> Choices,
    Usage? Usage,
    string? SystemFingerprint);

// Streaming chunk (SSE)
public sealed record ChatCompletionChunk(
    string Id, string Object, long Created, string Model,
    IReadOnlyList<ChatCompletionChunkChoice> Choices,
    Usage? Usage);                                 // set on final chunk only

// Errors
public sealed record ErrorResponse(ErrorBody Error);
public sealed record ErrorBody(string Message, string Type, string? Param, string? Code);
```

JSON naming follows OpenAI's snake_case convention via a shared `JsonSerializerOptions` with `JsonNamingPolicy.SnakeCaseLower`.

**Translation to MEAI.** The endpoint mapping lives in `Blaze.LlmGateway.Integrations/OpenAI/ChatCompletionsEndpoint.cs`:

```csharp
public static IEndpointRouteBuilder MapOpenAIChatCompletions(this IEndpointRouteBuilder routes)
{
    routes.MapPost("/v1/chat/completions", ChatCompletionsHandler.HandleAsync)
          .WithName("CreateChatCompletion")
          .Produces<ChatCompletionResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
          .WithOpenApi();
    return routes;
}
```

Handler responsibilities:

1. Bind `ChatCompletionRequest` using `ReadFromJsonAsync`.
2. Validate (`model` present, `messages` non-empty, at least one role in `{system,user,assistant,tool}`, etc.). Validation errors → 400 with `ErrorResponse`.
3. Translate to MEAI (`ChatMessage`, `ChatOptions` with tool_choice mapped to MEAI `ChatToolMode`, tools mapped via the tool plane).
4. Dispatch via `IChatClient` (the existing MEAI pipeline).
5. For streaming (`stream == true`): set `Content-Type: text/event-stream`, emit spec-compliant chunks (`data: {...}\n\n` plus `data: [DONE]\n\n`), include `finish_reason` on the last content chunk, honor client disconnects.
6. For non-streaming: accumulate the stream into a single `ChatCompletionResponse` with `usage` populated from the provider response.

**Error handling.** A shared `ExceptionToChatCompletionError` middleware converts known exceptions to the OpenAI error shape:

| Exception | HTTP | `error.type` | `error.code` |
|---|---|---|---|
| `ValidationException` | 400 | `invalid_request_error` | `invalid_request` |
| `AuthenticationException` | 401 | `authentication_error` | `invalid_api_key` |
| `AuthorizationException` | 403 | `permission_error` | `provider_not_allowed` |
| `RateLimitExceededException` | 429 | `rate_limit_error` | `rate_limit_exceeded` |
| `AllProvidersFailedException` | 503 | `api_error` | `all_providers_failed` |
| (other) | 500 | `api_error` | `internal_error` |

**Headers.** Document custom headers in `Blaze.LlmGateway.Integrations/OpenAI/Headers.cs`:

- `Authorization: Bearer <api-key>` — gateway auth (ADR-0008, ADR follow-on §Auth).
- `X-LlmGateway-Model` — optional override; bypasses routing (PRD FR-01-8).
- `X-LlmGateway-Session-Id` — optional session correlation (ADR-0004, used by agent plane only in Phase 3).
- `X-LlmGateway-NoCache: true` — bypass response cache (future, PRD FR-06-4).

**Deferred surfaces.** Tracked as follow-on ADRs to schedule after Phase 1 lands:

- `POST /v1/responses` (OpenAI Responses API) — durable session mapping.
- `POST /agents/{agentId}/messages` — A2A-style internal surface.
- `GET /mcp/tools` + `POST /mcp/invoke` — expose the tool plane as its own northbound MCP endpoint.
- `POST /v1/embeddings` — embeddings proxy (PRD non-goal today; may flip).

None of these are blocked by the Phase 1 Chat Completions surface; they compose on top of the same DTO translation layer.

## Consequences

**Positive**

- Off-the-shelf OpenAI clients (Python, JS, .NET SDK), Copilot CLI BYOM, Claude Code with custom baseURL, Codex, OpenCode — all work without vendor-specific client code.
- OpenAPI auto-generated from the DTOs — documentation for free.
- Typed DTOs replace fragile `JsonDocument` walking; fewer silent drops of request fields.
- Error contract matches what clients expect; Copilot CLI's own error-handling logic pairs naturally.

**Negative**

- Scope discipline: teams will ask for Responses/A2A immediately. The ADR is explicit that they are out of Phase 1 — we enforce that at review time.
- Adds ~8 DTO files plus validation code.

**Neutral**

- The [Program.cs](../../Blaze.LlmGateway.Api/Program.cs) endpoint mapping moves from minimal API inline to an extension method in the new `Integrations` project. Single-line call at composition root.

## Alternatives Considered

### Alternative A — Ship Chat Completions + Responses in Phase 1

Add OpenAI's session-oriented Responses API alongside Chat Completions. **Rejected** — Responses introduces durable session semantics which depend on ADR-0004's persistence substrate and the agent plane. Shipping it before Phase 3 forces the session store to freeze its schema under pressure; better to let the internal agent plane use the session store first, then expose the northbound Responses API once the contract is proven.

### Alternative B — Add A2A in Phase 1

Expose Agent Framework's A2A task/message protocol immediately so internal agents can call each other over HTTP. **Rejected** — same durability dependency, plus A2A requires the Azure Foundry agent adapter pattern from ADR-0006 to be production-shaped.

### Alternative C — Custom Blaze-native protocol

Invent a Blaze-specific JSON shape with richer metadata (routing hints, capability filters). **Rejected** — gives up the massive ecosystem advantage of OpenAI compatibility and requires every client to adopt a Blaze SDK. Any extra metadata can live in `metadata` or request headers without breaking spec.

## References

- [OpenAI Chat Completions API reference](https://platform.openai.com/docs/api-reference/chat)
- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §7 Integration plane
- [../../research/https-github-com-berriai-litellm.md](../../research/https-github-com-berriai-litellm.md) — OpenAI-compat proxy reference implementation
- [../../research/github-copilot-sdk.md](../../research/github-copilot-sdk.md) — Copilot CLI client expectations
