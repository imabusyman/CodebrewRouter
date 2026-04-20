---
applyTo: "Blaze.LlmGateway.AppHost/**, Blaze.LlmGateway.ServiceDefaults/**"
---

# Aspire AppHost guardrails

Apply to: Aspire resource wiring, provider secrets, OpenTelemetry / resilience / service-discovery defaults.

## Secret flow

```csharp
var azureFoundryApiKey = builder.AddParameter("azure-foundry-api-key", secret: true);
var githubModelsApiKey = builder.AddParameter("github-models-api-key", secret: true);
// ...
```

- Every secret goes through `builder.AddParameter("<name>", secret: true)`.
- Parameter names match `dotnet user-secrets` keys (e.g. `Parameters:azure-foundry-api-key`).
- Never hardcode a secret in AppHost code.

## Provider wiring recipe

A new provider adds exactly three things:

1. One `Add*()` resource call — `AddGitHubModel`, `AddAzureAIFoundry().RunAsFoundryLocal()`, `AddContainer`, etc.
2. `.WithReference(api)` so service discovery injects the endpoint.
3. `.WithEnvironment("LlmGateway__Providers__<Provider>__<Property>", parameterRef)` for each API key / endpoint override.

No inline HTTP client construction. No custom container orchestration. Keep AppHost declarative.

## Port + endpoint conventions

- Foundry Local: `http://localhost:5273`, API key `"notneeded"`.
- Ollama local container: `http://localhost:11434` on the `ollama/ollama` image, mounted `ollama-data:/root/.ollama`.
- Remote Ollama: `http://192.168.16.56:11434` (serves `Ollama` and `OllamaBackup` routes).
- GitHub Models: `https://models.inference.ai.azure.com`.

## ServiceDefaults

`AddServiceDefaults` wires OTLP, HTTP resilience, service discovery. New defaults go here, not in consumer projects.

OTLP exporter must target `localhost` or a LAN endpoint — never a public internet URL (ADR-0008).

## Verification

After any AppHost edit:

```powershell
dotnet build --no-incremental -warnaserror
dotnet run --project Blaze.LlmGateway.AppHost
# Verify dashboard loads; every resource green.
```
