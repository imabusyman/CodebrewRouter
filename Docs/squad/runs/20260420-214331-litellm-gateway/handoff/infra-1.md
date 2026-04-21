---
from: squad-orchestrator
to: Squad Infra
task: infrastructure.litellm-gateway-apphost
phase: Phase 1.3 (Parallel)
worktree: .worktrees/litellm-gateway-infra
branch: feature/litellm-gateway-infra
run_id: 20260420-214331-litellm-gateway
---

# Handoff: Infra — LiteLLM-Compatible Gateway Aspire Configuration

**Orchestrator:** squad-orchestrator  
**Agent:** Squad Infra subagent 1  
**Task:** Update AppHost and ServiceDefaults for Azure SDK + Swagger integration  
**Worktree:** `.worktrees/litellm-gateway-infra`  
**Branch:** `feature/litellm-gateway-infra`  
**Duration:** ~5 minutes  

---

## Scope

You are responsible for **Aspire orchestration and service defaults** for the LiteLLM gateway. Your changes are isolated to the `AppHost` and `ServiceDefaults` projects and a dedicated worktree branch.

### Files You May Edit (Exclusive Lock)

You have **exclusive edit rights** to these files:

1. **`Blaze.LlmGateway.AppHost/Program.cs`**
   - Add Aspire parameter for Azure OpenAI endpoint URL
   - Add Aspire parameter for Azure OpenAI API key
   - Wire Azure OpenAI SDK as resource with keyed DI binding
   - Register API project resource with parameter injection
   - Expose API at `http://localhost:5000` with live endpoints

2. **`Blaze.LlmGateway.ServiceDefaults/Extensions.cs`**
   - Add `AddOpenApiDocument()` or `AddSwaggerGen()` extension method
   - Register OpenAPI/Swagger service defaults
   - Configure service discovery for swagger endpoints

3. **`.worktrees/litellm-gateway-infra/**`** (worktree-specific)
   - Any temporary configuration files or scripts

### Files You Must NOT Edit

These files are owned by other agents. **Do not modify them:**

- `Blaze.LlmGateway.Api/**` (owned by Coder)
- `Blaze.LlmGateway.Tests/**` (owned by Tester)
- Any other project files not listed above

---

## Requirements (from PRD)

### Aspire AppHost Configuration

#### 1. Azure Parameter Registration

```csharp
var azureEndpoint = builder.AddParameter("azure-openai-endpoint", 
  secret: false, 
  defaultValue: "https://<your-instance>.openai.azure.com/");
  
var azureKey = builder.AddParameter("azure-openai-key", 
  secret: true, 
  defaultValue: "<your-api-key>");
```

#### 2. Azure SDK Resource Registration

```csharp
// Register Azure OpenAI SDK as resource with keyed service
var azureOpenAi = builder.AddAzureOpenAI("AzureFoundry")
  .WithParameter("endpoint", azureEndpoint)
  .WithParameter("key", azureKey);
```

Or, if manual wiring needed:

```csharp
builder.Services.AddKeyedSingleton<IChatClient>("AzureFoundry", (provider, key) =>
{
  var endpoint = azureEndpoint.Value;
  var apiKey = azureKey.Value;
  var client = new AzureOpenAIClient(
    new Uri(endpoint),
    new AzureKeyCredential(apiKey)
  );
  return client.AsBuilder().UseFunctionInvocation().Build();
});
```

#### 3. API Project Resource Registration

```csharp
builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
  .WithHttpEndpoint(port: 5000, name: "http")
  .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureEndpoint)
  .WithEnvironment("AZURE_OPENAI_KEY", azureKey);
```

#### 4. Service Defaults Extension

In `ServiceDefaults/Extensions.cs`:

```csharp
public static IServiceCollection AddSwaggerDefaults(this IServiceCollection services)
{
  services.AddOpenApiDocument(config =>
  {
    config.Title = "LiteLLM Gateway";
    config.Version = "1.0";
    config.Description = "LiteLLM-compatible OpenAI proxy gateway";
  });
  
  return services;
}
```

Or, using Swashbuckle:

```csharp
public static IServiceCollection AddSwaggerDefaults(this IServiceCollection services)
{
  services.AddSwaggerGen(config =>
  {
    config.SwaggerDoc("v1", new OpenApiInfo
    {
      Title = "LiteLLM Gateway",
      Version = "1.0"
    });
  });
  
  return services;
}
```

---

## Artifacts to Read

Before starting, read these to understand the spec:

1. **`Docs/squad/runs/20260420-214331-litellm-gateway/prd.md`** — Full PRD with Aspire requirements
2. **`Docs/squad/runs/20260420-214331-litellm-gateway/plan.md`** — Detailed plan
3. **`Blaze.LlmGateway.AppHost/Program.cs`** — Existing AppHost structure (reference)
4. **`Blaze.LlmGateway.ServiceDefaults/Extensions.cs`** — Existing service defaults (reference)
5. **`CLAUDE.md`** — Code style and Aspire patterns
6. **Coder's handoff** (`handoff/coder-1.md`) — Azure SDK integration details

---

## Expected API Contract (from Coder)

The API project will expose:

- `POST /v1/chat/completions` — Chat endpoint (streaming)
- `POST /v1/completions` — Text completions (streaming)
- `GET /v1/models` — Model discovery
- `GET /swagger/ui` — Swagger UI
- `GET /openapi.json` — OpenAPI schema

Your task is to ensure Aspire wires all these correctly and makes them accessible via `http://localhost:5000`.

---

## Acceptance Criteria

✅ **Azure Parameter Registration:**
- [ ] `azure-openai-endpoint` parameter created (secret: false)
- [ ] `azure-openai-key` parameter created (secret: true)
- [ ] Parameters have sensible defaults (or prompts for user input)

✅ **Azure SDK Resource:**
- [ ] Azure OpenAI SDK registered as keyed service `"AzureFoundry"`
- [ ] Endpoint URL injected from parameter
- [ ] API key injected from parameter
- [ ] SDK wrapped with `.UseFunctionInvocation()` for MEAI compatibility

✅ **API Project Resource:**
- [ ] API project registered in AppHost
- [ ] HTTP endpoint exposed on port 5000 (or configurable)
- [ ] Azure parameters passed as environment variables to API
- [ ] API project starts without errors

✅ **Service Defaults:**
- [ ] Swagger/OpenAPI service defaults added
- [ ] OpenAPI document title set to "LiteLLM Gateway"
- [ ] All endpoints documented in Swagger

✅ **Aspire Orchestration:**
- [ ] `dotnet run --project Blaze.LlmGateway.AppHost` starts without errors
- [ ] Aspire dashboard displays all resources
- [ ] API accessible at `http://localhost:5000`
- [ ] Swagger UI accessible at `http://localhost:5000/swagger/ui`
- [ ] OpenAPI schema accessible at `http://localhost:5000/openapi.json`

---

## Inherited Assumptions

| Assumption | Status |
|-----------|--------|
| Azure SDK `Azure.AI.OpenAI` already available | ✅ |
| Aspire AppHost supports parameter injection | ✅ |
| Swagger/NSwag available in ServiceDefaults | ✅ |
| Port 5000 available for API | ✅ |
| Coder agent implements endpoints on-time | ✅ (Awaited) |

---

## Common Gotchas

1. **Keyed DI:** Use `AddKeyedSingleton<IChatClient>("AzureFoundry", ...)` to match Coder's expectation
2. **Parameter Secrets:** Mark `azure-openai-key` as `secret: true` for secure handling
3. **Environment Variables:** Ensure Coder's Program.cs reads from injected environment variables (or Aspire `.WithParameter()` passes them)
4. **Port Conflict:** If port 5000 is in use, update to 5001+ and update docs
5. **Swagger Middleware:** Ensure `Program.cs` includes Swagger middleware; you just configure defaults

---

## Optional: Local Azure Emulator

If actual Azure credentials are not available, consider:

1. **Azure Cosmos Emulator** or **Azurite** for local testing
2. **Mock Azure SDK** for unit tests (Tester agent handles this)
3. **Default values** in parameters for demo purposes

---

## Checklist

- [ ] Read PRD, plan, Coder handoff, and this handoff
- [ ] Create worktree: `git worktree add -b feature/litellm-gateway-infra .worktrees/litellm-gateway-infra main`
- [ ] Switch to worktree
- [ ] Update `AppHost/Program.cs`:
  - [ ] Add Azure parameter registration
  - [ ] Add Azure SDK resource registration
  - [ ] Add API project resource with parameter injection
- [ ] Update `ServiceDefaults/Extensions.cs`:
  - [ ] Add Swagger/OpenAPI service defaults
  - [ ] Register defaults in API project
- [ ] Test locally: `dotnet run --project Blaze.LlmGateway.AppHost`
  - [ ] Aspire dashboard accessible
  - [ ] API starts without errors
  - [ ] Endpoints accessible via `http://localhost:5000`
- [ ] Verify Swagger UI: `http://localhost:5000/swagger/ui`
- [ ] Commit changes: `git add -A && git commit -m "infra: Configure Aspire AppHost + Azure SDK + Swagger"`
- [ ] Emit `[DONE]` signal when complete

---

## Success Signal

When all acceptance criteria are met, emit:

```
[DONE] Task: Infra — Aspire AppHost + ServiceDefaults configuration
Status: ✅ Azure SDK wired, parameters injected, Swagger configured, API accessible
Artifacts: AppHost/Program.cs updated, ServiceDefaults/Extensions.cs updated
Reason: Aspire orchestration working, API accessible at localhost:5000, Swagger UI live
```

---

## Appendix: Aspire Pattern Reference

### AddKeyedSingleton Pattern (MEAI Compliance)

```csharp
// In AppHost
builder.Services.AddKeyedSingleton<IChatClient>("AzureFoundry", 
  (provider, key) => new AzureOpenAIClient(...).AsBuilder().Build());

// In Api/Program.cs (Coder task)
[ApiController, Route("/v1")]
public class ChatCompletionsEndpoint(
  [FromKeyedServices("AzureFoundry")] IChatClient azureClient,
  IRoutingStrategy routingStrategy) : ControllerBase
{
  // Use azureClient or route via IRoutingStrategy
}
```

### Parameter Injection Pattern

```csharp
// In AppHost
var azureEndpoint = builder.AddParameter("azure-openai-endpoint", secret: false);

// Inject into API via environment variable
builder.AddProject<Projects.Blaze_LlmGateway_Api>("api")
  .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureEndpoint);

// In Api/Program.cs (Coder task)
var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
```

---

*Handoff created by squad-orchestrator at 2026-04-20T21:43:45Z*  
*Run ID: 20260420-214331-litellm-gateway*
