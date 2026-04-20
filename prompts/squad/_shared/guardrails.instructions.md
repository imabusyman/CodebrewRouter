---
applyTo: "**"
---

# Squad guardrails (universal)

Every squad specialist obeys these rules. Path-scoped guardrails (below) layer on top of this file.

## 1. MEAI is the law

- All LLM interaction flows through `IChatClient`, `ChatMessage`, `ChatOptions`, `ChatRole` from `Microsoft.Extensions.AI`.
- **No raw `HttpClient`** for LLM calls. Ever.
- **No custom tool-calling loops.** Use MEAI's `FunctionInvokingChatClient`.
- New middleware **inherits `DelegatingChatClient`**. Never implement `IChatClient` directly.

## 2. Streaming by default

- `POST /v1/chat/completions` is SSE. Terminator is `data: [DONE]\n\n`.
- Use `GetStreamingResponseAsync` + `IAsyncEnumerable<ChatResponseUpdate>`. `CompleteStreamingAsync` / `CompleteAsync` no longer exist in current MEAI.
- Never buffer a streaming response end-to-end.

## 3. Keyed DI for providers

- 9 providers registered as keyed `IChatClient` services with the `RouteDestination` enum name as the key.
- Each keyed provider wraps with `.AsBuilder().UseFunctionInvocation().Build()` individually.
- Resolve inside middleware via `IServiceProvider.GetRequiredKeyedService<IChatClient>("ProviderName")`.

## 4. Cloud egress (ADR-0008)

- Default-deny. Every cloud-provider call requires an allow-list check on `ClientIdentity` before dispatch.
- Every new `RouteDestination` needs a Locality classification (local / lan / cloud) plus an ADR amendment.

## 5. Squad protocol

- Specialists are invoked by the Conductor with a `[CONDUCTOR]` prefix.
- Respond only with structured-action tags: `[ASK]`, `[CREATE]`, `[EDIT]`, `[CHECKPOINT]`, `[BLOCKED]`, `[DONE]`.
- Reread artifacts from disk per the envelope. Ignore prior chat context.
- Stay within the envelope's exclusive file-lock. Touching anything else → `[BLOCKED]` with the path you need.

## 6. Quality gate

Before any `[DONE]` that involves code:

```powershell
dotnet build --no-incremental -warnaserror
dotnet test --no-build --collect:"XPlat Code Coverage"
```

Zero tolerance for `CS8600`–`CS8629` nullable warnings. No `#nullable disable`, no `#pragma warning disable`.
