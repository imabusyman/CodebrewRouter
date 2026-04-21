---
applyTo: "Blaze.LlmGateway.Infrastructure/**, Blaze.LlmGateway.Api/**, Blaze.LlmGateway.Core/**"
---

# MEAI infrastructure guardrails

Apply to: routing middleware, provider registration, MEAI pipeline construction, API host wiring, domain/config types.

## Middleware pipeline (outermost → innermost)

```
McpToolDelegatingClient       ← injects MCP tools into ChatOptions (unkeyed IChatClient)
  └── LlmRoutingChatClient    ← resolves target provider via IRoutingStrategy
        └── [Keyed IChatClient].UseFunctionInvocation()  ← actual model call
```

`FunctionInvokingChatClient` is attached per keyed provider via `.AsBuilder().UseFunctionInvocation().Build()` — not as a shared outer layer.

## Required SDK mappings

| Provider | SDK | Registration |
|---|---|---|
| `AzureFoundry`, `FoundryLocal` | `AzureOpenAIClient` | `.AsChatClient()` |
| `Ollama`, `OllamaBackup`, `OllamaLocal` | `OllamaApiClient` | `.AsChatClient()` |
| `GithubCopilot`, `GithubModels`, `OpenRouter` | `OpenAIClient` (custom endpoint) | `.AsChatClient()` |
| `Gemini` | `Google.GenAI.Client` | `.AsIChatClient()` |

Deviating from these mappings is a CRITICAL architectural finding.

## Method signatures (current MEAI)

```csharp
public override Task<ChatResponse> GetResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default);

public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
    IEnumerable<ChatMessage> messages,
    ChatOptions? options = null,
    CancellationToken cancellationToken = default);
```

## Program.cs discipline

- `Program.cs` stays minimal. DI wiring lives in `Infrastructure/InfrastructureServiceExtensions.cs`.
- API surface: `builder.Services.AddLlmProviders(...)` + `builder.Services.AddLlmInfrastructure(...)`.

## Cancellation + configuration

- Propagate `CancellationToken` end-to-end. No `.Wait()` / `.Result` / `GetAwaiter().GetResult()`.
- Provider settings come from `IOptions<LlmGatewayOptions>` bound to the `"LlmGateway"` config section.

## Hard don'ts

- No raw `HttpClient` for LLM calls.
- No custom tool-calling loops.
- No `#nullable disable`, no `#pragma warning disable`.
- No direct `IChatClient` implementation — always inherit `DelegatingChatClient`.
