# ADR-0008: Cloud escalation policy — default-deny with per-client auth-bound allow-list

- **Status:** Proposed
- **Date:** 2026-04-17
- **Deciders:** Architecture, Security
- **Related:** ADR-0002, ADR-0003, ADR-0007, [plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Proposed epics", §"Cloud escalation ADR"

## Context

The north-star plan locks two policy decisions:

- **Local-first and LAN-aware execution** is a first-class property.
- **Cloud providers are explicit allow rules**, not the default path.

The current implementation does not enforce this. [OllamaMetaRoutingStrategy.cs](../../Blaze.LlmGateway.Infrastructure/RoutingStrategies/OllamaMetaRoutingStrategy.cs) and [KeywordRoutingStrategy.cs](../../Blaze.LlmGateway.Infrastructure/RoutingStrategies/KeywordRoutingStrategy.cs) can freely return `RouteDestination.Gemini`, `GithubCopilot`, `OpenRouter`, or `AzureFoundry` — all cloud destinations — without any consent check. In a LAN-only deployment that is a data-egress bug waiting to happen.

Two policy shapes to choose between:

1. **Default-allow, deny-list** — everything is cloud-routable unless configured otherwise. Status quo. Easiest to extend, most dangerous.
2. **Default-deny, allow-list** — cloud is off unless the client has explicit permission. More config up front, stronger posture.

The PRD FR-04 subgroup (auth, per-key allowlist) already implies per-key scoping. Cloud escalation is a natural fit as a further dimension of that same allow-list.

## Decision

We will **default-deny cloud destinations. Each authenticated client identity ships with an explicit allow-list of cloud provider IDs it is permitted to use. Every routing decision is checked against the client's allow-list, and every escalation (cloud hit) is logged with the deciding allow-list entry.**

### Details

**`ProviderLocality` tagging.** From ADR-0002, each `ProviderDescriptor` carries a `Locality: ProviderLocality` field with values `Local`, `Lan`, or `Cloud`. This is the authoritative classification. Cloud examples: `azure-foundry`, `github-copilot`, `github-models`, `gemini`, `openrouter`, and any Claude/Codex provider added later.

**Client identity.** Established by the auth middleware (designed in the follow-on Auth ADR). For this ADR we assume a `ClientIdentity` exists and carries:

```csharp
public sealed record ClientIdentity(
    string ClientId,
    string DisplayName,
    IReadOnlyList<string> AllowedProviderIds,  // allow-list by provider ID
    IReadOnlyList<string>? AllowedModelIds,    // optional, tighter than provider
    CloudEscalationPolicy CloudPolicy);

public enum CloudEscalationPolicy
{
    Denied,                 // may never route to Cloud providers
    AllowListed,            // may only route to Cloud providers in AllowedProviderIds
    Unrestricted            // reserved for internal admin clients
}
```

Config shape:

```json
{
  "LlmGateway": {
    "Clients": [
      {
        "ClientId": "copilot-cli-internal",
        "DisplayName": "Internal Copilot CLI users",
        "ApiKeys": [ "${key-store:copilot-cli-key}" ],
        "CloudPolicy": "AllowListed",
        "AllowedProviderIds": [ "ollama-lan", "foundry-local", "github-models" ]
      },
      {
        "ClientId": "claude-code-internal",
        "DisplayName": "Claude Code users",
        "ApiKeys": [ "${key-store:claude-code-key}" ],
        "CloudPolicy": "Denied",
        "AllowedProviderIds": [ "ollama-lan", "foundry-local" ]
      },
      {
        "ClientId": "admin",
        "ApiKeys": [ "${key-store:admin-key}" ],
        "CloudPolicy": "Unrestricted",
        "AllowedProviderIds": []
      }
    ]
  }
}
```

**Enforcement point.** A new `CloudEscalationDelegatingChatClient` (MEAI `DelegatingChatClient`) is inserted into the pipeline **between** `LlmRoutingChatClient` and `McpToolDelegatingClient`:

```
McpToolDelegatingClient
  └── CloudEscalationDelegatingChatClient       ← NEW
        └── LlmRoutingChatClient
              └── keyed provider client
```

It consults `RoutingContext.ClientId` (populated upstream by auth middleware) and the target `ProviderDescriptor` (populated by the routing strategy). If the target provider has `Locality = Cloud` and the client policy is `Denied`, or the provider ID is not in `AllowedProviderIds` when policy is `AllowListed`, the call fails with `AuthorizationException("provider_not_allowed: <providerId>")` which maps to HTTP 403 per ADR-0003's error table.

**Fallback fallout.** When a routing strategy selects a provider that is blocked by policy, the resilience chain must **skip, not fail**. The master doc §4 specifies: "cloud-escalation denial is a fallback-chain hop, not a terminal error — try the next eligible provider and fail only if the chain is exhausted." The `AggregateException` case then carries all denials for observability.

**Meta-router guidance.** `OllamaMetaRoutingStrategy` gets an updated system prompt that tells the router "you are running in a LAN environment; prefer `ollama-lan` and `foundry-local` unless the user explicitly names a cloud provider." The default selection pressure therefore matches the policy default.

**Observability.** Every escalation (cloud hit) emits an OTel event attribute: `llm.escalation.allowed=true|false`, `llm.escalation.client_id`, `llm.escalation.provider_id`. Denied escalations log at `Warning`; allowed ones at `Information`. Denied-escalation counts are a dashboard metric.

**Phase 1 bootstrap.** Auth middleware is not on the Phase-1 must-land list. For Phase-1 only, a single implicit "default" client identity is created with `CloudPolicy = AllowListed` and the admin's configured `AllowedProviderIds`. This lets the cloud-escalation check run before auth fully lands. Once auth is in (follow-on ADR), the default client is removed.

## Consequences

**Positive**

- Policy-enforced local-first. A misconfigured or compromised client cannot silently exfiltrate prompts to the cloud.
- Per-client blast radius for cost, audit, and compliance.
- Consistent with the planning-draft mandate that cloud is an explicit allow rule.
- Pairs naturally with the upcoming auth ADR (FR-04), the rate-limiting ADR (FR-07), and the observability schema.

**Negative**

- Extra config for every client. Operator overhead. Mitigation: a config generator (`dotnet blaze client add`) in Phase 5.
- A client whose allow-list is wrong sees `provider_not_allowed` errors that look like the gateway is broken. Clear error messages + an admin dashboard reduce friction.

**Neutral**

- Adds `CloudEscalationDelegatingChatClient` (~100 LOC) and a `ClientIdentityAccessor` scoped service.
- `IRoutingStrategy.RoutingContext` (per ADR-0002) picks up `ClientIdentity` — used only by resilience and policy code, not by the router LLM prompt.

## Alternatives Considered

### Alternative A — Default-allow with deny-list

Flip the polarity. **Rejected** — fails-open on misconfiguration. For an LLM gateway the default behavior on ambiguity should protect data, not maximize reach.

### Alternative B — Static global allow-list, no per-client scoping

A single `Routing.AllowedCloudProviders` list applied to everyone. **Rejected** — different clients have different risk profiles (internal automation vs. developer laptops vs. third-party BYOM). Collapsing them to one list forces either over-permissioning or constant config churn.

### Alternative C — Enforce at the auth middleware, not in the pipeline

Reject the request at the edge. **Rejected** — the blocked destination is only known after routing resolves (meta-router picks it dynamically). Enforcement must live in the pipeline, not at the edge. Auth still runs first to establish identity, but policy evaluation happens per-turn against the routing decision.

## References

- [../tech-design/blaze-llmgateway-architecture.md](../tech-design/blaze-llmgateway-architecture.md) §4 Inference plane, §8 Cross-cutting
- [../../plan/llm-agent-platform-plan.md](../../plan/llm-agent-platform-plan.md) §"Cloud escalation ADR"
- [../../PRD/blaze-llmgateway-prd.md](../../PRD/blaze-llmgateway-prd.md) FR-04-3 (per-key provider allowlist)
