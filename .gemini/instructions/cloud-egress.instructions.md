---
applyTo: "**/*.cs, **/appsettings*.json, Blaze.LlmGateway.AppHost/**"
---

# Cloud-egress guardrails (ADR-0008 default-deny)

Apply to: all production C#, all `appsettings*.json`, all AppHost code.

## Default-deny rule

Any request that would leave the device or LAN to reach a cloud provider (AzureFoundry, GithubCopilot, GithubModels, OpenRouter, Gemini) is **denied unless**:

1. The caller presents a `ClientIdentity` whose `AllowedProviderIds` contains the target `RouteDestination`, AND
2. The target provider's Locality is declared in an ADR amendment (see ADR-0002 provider-identity-model).

Local and LAN providers (`FoundryLocal`, `OllamaLocal`, `Ollama`, `OllamaBackup`) do not require the allow-list check.

## Required check pattern

```csharp
if (destination.IsCloud()
    && !clientIdentity.AllowedProviderIds.Contains(destination))
{
    logger.LogWarning("Cloud egress denied: {ClientId} → {Destination}", clientIdentity.Id, destination);
    throw new CloudEgressDeniedException(destination, clientIdentity);
}
```

The check must fire **before** any `IChatClient.Get(Streaming)ResponseAsync` call on a cloud provider.

## Adding a new RouteDestination

New entries to the `RouteDestination` enum require:

- A Locality classification (`local` | `lan` | `cloud`) captured in an ADR-0002 amendment or a new ADR.
- An ADR-0008 allow-list amendment — either adding the provider or explicitly recording "default deny, no client allowed".
- Matching DI key, router output text, and config section — all three must use the exact enum name.

Missing any one of these is a CRITICAL Security-Review finding.

## Configuration

- Secrets never appear in `appsettings*.json`. They live in `dotnet user-secrets` and flow through `Aspire.AddParameter(..., secret: true)`.
- Endpoint URLs in `appsettings*.json` must point at localhost, a LAN address, or an already-approved cloud endpoint.

## Telemetry

OTLP exporter targets must resolve to localhost or a LAN address. A new endpoint pointing anywhere else triggers a MEDIUM Security-Review finding (potential telemetry leak).

## Streaming failover

If a mid-stream failover path is introduced, never surface provider-specific error text that may include internal URLs or API keys. Log raw error to `ILogger<T>`; emit a sanitized SSE error event to the client.
