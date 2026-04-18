# ADR-0002: Provider identity model — config-driven descriptors replace `RouteDestination` enum

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture
- **Related:** ADR-0001, ADR-0005, [PRD §2.1, §9.3](../../PRD/blaze-llmgateway-prd.md), [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Phase 2 — Provider/model catalog"

## Context

Today provider identity is expressed by a closed enum [Blaze.LlmGateway.Core/RouteDestination.cs](../../Blaze.LlmGateway.Core/RouteDestination.cs):

```csharp
public enum RouteDestination { AzureFoundry, Ollama, OllamaBackup, GithubCopilot, Gemini, OpenRouter, FoundryLocal, GithubModels, OllamaLocal }
```

Every provider is hard-wired in three places:

1. The enum itself.
2. A hand-rolled DI registration in [InfrastructureServiceExtensions.AddLlmProviders()](../../Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs).
3. A strongly-typed options class in [LlmGatewayOptions.cs](../../Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs).

The router strategies (`OllamaMetaRoutingStrategy`, `KeywordRoutingStrategy`) also pattern-match on the enum values.

Phase 2 of the north-star plan demands:

- LM Studio, llama.cpp, and multiple Gemma-4 variants as first-class providers (see ADR-0005).
- Capability-based routing (context window size, vision support, tool-call support, cost class, locality).
- Runtime provider enable/disable (PRD FR-10-2).
- A single provider can host multiple models (one Ollama endpoint serves `llama3.2`, `qwen2.5-coder`, `gemma2`).

Continuing to grow the enum against those requirements would mean touching at least three files for every new model and inventing synthetic enum values like `OllamaQwen25` or `FoundryLocalGemma4` — classic smell.

## Decision

We will **replace the closed enum with a config-driven provider catalog**, built on three records: `ProviderDescriptor`, `ModelProfile`, and `CapabilityMetadata`. Provider identity becomes a **string ID** sourced from configuration, not a compile-time enum.

### Details

**Core domain types.** Land in `Blaze.LlmGateway.Core/Providers/`:

```csharp
// ProviderDescriptor.cs
public sealed record ProviderDescriptor(
    string Id,                         // "ollama-lan", "foundry-local", "lmstudio-workstation"
    ProviderKind Kind,                 // Ollama, OpenAICompatible, AzureOpenAI, Gemini
    string Endpoint,
    string? ApiKey,                    // null => use ambient credential / "notneeded"
    IReadOnlyList<ModelProfile> Models,
    bool Enabled = true,
    ProviderLocality Locality = ProviderLocality.Local);

public enum ProviderKind { Ollama, OpenAICompatible, AzureOpenAI, Gemini }
public enum ProviderLocality { Local, Lan, Cloud }

// ModelProfile.cs
public sealed record ModelProfile(
    string Id,                         // "ollama-lan/llama3.2", "foundry-local/phi-4-mini"
    string ProviderId,                 // "ollama-lan"
    string ModelName,                  // "llama3.2"
    CapabilityMetadata Capabilities,
    ModelPricing? Pricing = null);     // null = free / self-hosted

// CapabilityMetadata.cs
public sealed record CapabilityMetadata(
    int ContextWindowTokens,
    bool SupportsStreaming = true,
    bool SupportsToolCalls = false,
    bool SupportsVision = false,
    bool SupportsEmbeddings = false,
    string? FamilyTag = null);         // "gemma-4", "llama-3", "phi-4", "gpt-4o"

// ModelPricing.cs (cloud providers only)
public sealed record ModelPricing(
    decimal InputCostPer1K,
    decimal OutputCostPer1K,
    string Currency = "USD");
```

**Configuration binding.** `LlmGatewayOptions` gains a `Providers: ProviderDescriptor[]` array bound from `appsettings.json`:

```json
{
  "LlmGateway": {
    "Providers": [
      {
        "Id": "ollama-lan",
        "Kind": "Ollama",
        "Endpoint": "http://192.168.16.56:11434",
        "Locality": "Lan",
        "Models": [
          { "Id": "ollama-lan/router",   "ModelName": "router",   "Capabilities": { "ContextWindowTokens": 8192 } },
          { "Id": "ollama-lan/llama3.2", "ModelName": "llama3.2", "Capabilities": { "ContextWindowTokens": 128000, "SupportsToolCalls": true } }
        ]
      },
      { "Id": "foundry-local", "Kind": "OpenAICompatible", "Endpoint": "http://localhost:5273", "ApiKey": "notneeded", "Locality": "Local", "Models": [ ... ] },
      { "Id": "azure-foundry", "Kind": "AzureOpenAI", "Endpoint": "https://...", "Locality": "Cloud", "Enabled": false, "Models": [ ... ] }
    ],
    "Routing": { "RouterModelId": "ollama-lan/router", "FallbackModelId": "ollama-lan/llama3.2" }
  }
}
```

The old per-provider options classes (`OllamaOptions`, `GeminiOptions`, etc.) are **deleted**. `AzureFoundryOptions.ApiKey` semantics (null → DefaultAzureCredential) survive as a flag on `ProviderDescriptor` (`ApiKey == null` → credential chain).

**Provider registry.** Replace [AddLlmProviders()](../../Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs) with a catalog walker:

```csharp
// Blaze.LlmGateway.Infrastructure/Providers/ProviderRegistry.cs
public interface IProviderRegistry
{
    IChatClient GetChatClient(string modelId);
    IReadOnlyCollection<ProviderDescriptor> Providers { get; }
    IReadOnlyCollection<ModelProfile> Models { get; }
}

internal sealed class ProviderRegistry(IOptionsMonitor<LlmGatewayOptions> options, IProviderClientFactory factory) : IProviderRegistry
{
    // Resolves ModelProfile.Id -> IChatClient. Builds lazily, caches per model.
    // Supports IOptionsMonitor change notifications => Phase 3 hot-reload.
}

// Blaze.LlmGateway.Infrastructure/Providers/IProviderClientFactory.cs
public interface IProviderClientFactory
{
    IChatClient Create(ProviderDescriptor descriptor, ModelProfile model);
}
```

`ProviderClientFactory` owns the switch on `ProviderKind`, performing the SDK-specific wiring rules from [CLAUDE.md](../../CLAUDE.md) (AzureOpenAI → `AzureOpenAIClient`, Ollama → `OllamaApiClient`, etc.). Keyed DI moves to internal cache inside `ProviderRegistry` — no more public string keys.

**Routing strategies.** `IRoutingStrategy.ResolveAsync` returns `string modelId` instead of `RouteDestination`:

```csharp
public interface IRoutingStrategy
{
    Task<string> ResolveAsync(IEnumerable<ChatMessage> messages, RoutingContext ctx, CancellationToken ct = default);
}

public sealed record RoutingContext(
    string? RequestedModelId,          // X-LlmGateway-Model header override (FR-01-8)
    ClientIdentity ClientId,           // for cloud escalation policy (ADR-0008)
    IReadOnlyList<string> RequiredCapabilities);
```

**Migration path (enum alias).** For one release cycle, keep `RouteDestination` as a `[Obsolete]` string-alias helper that maps to model IDs (`RouteDestination.Ollama → "ollama-lan/llama3.2"`). Router strategies implement the new interface via adapters over the old enum values so existing unit tests keep compiling. Remove the enum in the release that closes Phase 2.

**Tests.** Existing [LlmRoutingChatClientTests](../../Blaze.LlmGateway.Tests/LlmRoutingChatClientTests.cs) and [OllamaMetaRoutingStrategyTests](../../Blaze.LlmGateway.Tests/OllamaMetaRoutingStrategyTests.cs) port to the new interface by constructing `ProviderRegistry` with fake descriptors; this also eliminates the tests' dependency on the live DI container.

## Consequences

**Positive**

- Adding a provider or model becomes a config edit. No code change, no enum value, no DI registration code. Directly addresses PRD NFR-06 (extensibility) and PRD FR-10-2 (runtime enable/disable).
- Capability-based routing (FR-01-7) is expressible: "route to the cheapest model with `SupportsToolCalls=true` and `ContextWindowTokens >= 32000`."
- Multiple models per endpoint are natural (Ollama hosting both `router` and `llama3.2`).
- Opens the door to Phase 5's per-tenant provider pools — descriptors can carry a `Tenants: []` allowlist.

**Negative**

- Breaking change to `RouteDestination` (public enum). Consumers relying on enum values must migrate to string IDs. Mitigated by the obsolete-alias bridge.
- Configuration validation becomes richer: must check that `Routing.RouterModelId` points to an enabled model, that `ModelProfile.Id` contains a `/`, that no two descriptors share an `Id`. Add an `IValidateOptions<LlmGatewayOptions>` implementation with clear error messages.
- Provider secrets are now an array, not strongly-typed fields, which breaks the current Aspire `WithEnvironment("LlmGateway__Providers__AzureFoundry__ApiKey", ...)` pattern. Update [AppHost/Program.cs](../../Blaze.LlmGateway.AppHost/Program.cs) to emit `LlmGateway__Providers__0__ApiKey`-style keys, or switch to an indexed-secret pattern keyed on provider ID.

**Neutral**

- Logging includes the new `modelId` field; observability spans (per ADR, §8) must be updated to drop `provider` in favour of `llm.provider.id` + `llm.model.id`.

## Alternatives Considered

### Alternative A — Keep the enum, add a ModelProfile sidecar

Extend the enum and add a separate `ModelProfile` lookup keyed by enum value. **Rejected** — still grows the enum on every new provider/model and forces code changes; capability routing must re-derive model → enum mapping which is awkward.

### Alternative B — Interface-per-provider + reflection discovery

Define `IProviderPlugin` and load assemblies at runtime. **Rejected** — plugin assembly loading is a large blast radius, security risk (arbitrary code in the gateway), overkill for the current set of providers, and the MEAI `IChatClient` abstraction already gives us provider polymorphism.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §4 Inference plane
- [../../research/https-github-com-berriai-litellm.md](../../research/https-github-com-berriai-litellm.md) — LiteLLM catalog/router pattern
- [../../research/https-github-com-microsoft-foundry-local.md](../../research/https-github-com-microsoft-foundry-local.md) — Foundry Local control-plane/data-plane split
- [../../plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Phase 2 — Provider/model catalog"
