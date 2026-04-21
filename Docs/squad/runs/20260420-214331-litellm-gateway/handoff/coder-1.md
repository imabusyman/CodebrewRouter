---
from: squad-orchestrator
to: Squad Coder
task: implementation.litellm-gateway-endpoints
phase: Phase 1.1 (Parallel)
worktree: .worktrees/litellm-gateway-coder
branch: feature/litellm-gateway-coder
run_id: 20260420-214331-litellm-gateway
---

# Handoff: Coder — LiteLLM-Compatible Gateway Endpoints

**Orchestrator:** squad-orchestrator  
**Agent:** Squad Coder subagent 1  
**Task:** Implement 3 LiteLLM-compatible endpoints + Azure OpenAI SDK integration  
**Worktree:** `.worktrees/litellm-gateway-coder`  
**Branch:** `feature/litellm-gateway-coder`  
**Duration:** ~10 minutes  

---

## Scope

You are responsible for **endpoint implementation and Azure SDK integration**. Your changes are isolated to the `Api` project and a dedicated worktree branch.

### Files You May Edit (Exclusive Lock)

You have **exclusive edit rights** to these files:

1. **`Blaze.LlmGateway.Api/Program.cs`**
   - Add Swagger/NSwag configuration
   - Register all 3 new endpoints
   - Wire Azure OpenAI SDK as keyed service `"AzureFoundry"`
   - Add CORS, middleware, service registration

2. **`Blaze.LlmGateway.Api/Endpoints/ChatCompletionsEndpoint.cs`** (create)
   - Implement `POST /v1/chat/completions`
   - Accept OpenAI-compatible chat request
   - Stream SSE response with `data: [DONE]` terminator
   - Route request to LlmRoutingChatClient

3. **`Blaze.LlmGateway.Api/Endpoints/CompletionsEndpoint.cs`** (create)
   - Implement `POST /v1/completions`
   - Accept legacy text-only prompt
   - Stream SSE text completions
   - Route request to LlmRoutingChatClient

4. **`Blaze.LlmGateway.Api/Endpoints/ModelsEndpoint.cs`** (create)
   - Implement `GET /v1/models`
   - Return JSON array of available providers + models
   - Query from Azure SDK + existing providers (Ollama, Gemini, etc.)

5. **`Blaze.LlmGateway.Api/Models/OpenAiModels.cs`** (create)
   - Define DTOs for LiteLLM-compatible request/response schemas
   - ChatCompletionRequest, ChatCompletionResponse, CompletionResponse, ModelList, etc.
   - Use record types; follow existing C# style (primary constructors)

6. **`.worktrees/litellm-gateway-coder/**`** (worktree-specific)
   - Any temporary files for testing/validation

### Files You Must NOT Edit

These files are owned by other agents. **Do not modify them:**

- `Blaze.LlmGateway.Tests/**` (owned by Tester)
- `Blaze.LlmGateway.AppHost/**` (owned by Infra)
- `Blaze.LlmGateway.ServiceDefaults/**` (owned by Infra)
- Any other project files not listed above

---

## Requirements (from PRD)

### Endpoint Contracts

#### POST /v1/chat/completions
**Request (OpenAI-compatible):**
```json
{
  "model": "gpt-4",
  "messages": [
    {"role": "system", "content": "You are helpful."},
    {"role": "user", "content": "Hello"}
  ],
  "temperature": 0.7,
  "max_tokens": 100,
  "stream": true
}
```

**Response (streaming SSE):**
```
data: {"choices":[{"delta":{"content":"Hello"}}]}
data: {"choices":[{"delta":{"content":" there"}}]}
data: [DONE]
```

#### POST /v1/completions
**Request:**
```json
{
  "model": "gpt-3.5-turbo",
  "prompt": "Once upon a time",
  "max_tokens": 50,
  "stream": true
}
```

**Response (streaming SSE):**
```
data: {"choices":[{"text":" in"}]}
data: {"choices":[{"text":" a"}]}
data: [DONE]
```

#### GET /v1/models
**Response:**
```json
{
  "object": "list",
  "data": [
    {"id": "gpt-4", "object": "model", "provider": "AzureFoundry"},
    {"id": "gpt-3.5-turbo", "object": "model", "provider": "Ollama"},
    {"id": "phi-4-mini", "object": "model", "provider": "GithubModels"}
  ]
}
```

### Azure SDK Integration

1. **Register** `Azure.AI.OpenAI.AzureOpenAIClient` as keyed service `"AzureFoundry"`
2. **Wrap** with `.AsBuilder().UseFunctionInvocation().Build()`
3. **Inject** endpoint URL + API key from Aspire parameters (will be set by Infra agent)
4. **Route** requests via `LlmRoutingChatClient` to appropriate provider

### Swagger/OpenAPI

1. **Add** NSwag or Swashbuckle configuration to `Program.cs`
2. **Register** Swagger endpoint at `/swagger/ui`
3. **Serve** OpenAPI schema at `/openapi.json`
4. **Document** all endpoints with request/response examples

### Code Style & Quality

- Follow existing C# conventions: **primary constructors**, **collection expressions**, **CancellationToken propagation**
- Use **record types** for DTOs
- **Stream-first design**: Prefer streaming responses; support non-streaming fallback
- **Error handling**: Return appropriate HTTP status codes (400, 401, 500)
- **No warnings:** Code must compile with `-warnaserror`
- **MEAI Compliance:** Use IChatClient, ChatMessage, ChatOptions, ChatRole consistently
- **ADR-0008 Default-Deny:** No cloud-egress without explicit justification

---

## Artifacts to Read

Before starting, read these to understand the codebase:

1. **`Docs/squad/runs/20260420-214331-litellm-gateway/prd.md`** — Full PRD
2. **`Docs/squad/runs/20260420-214331-litellm-gateway/plan.md`** — Detailed plan with file-lock matrix
3. **`CLAUDE.md`** — Codebase style guide and MEAI law
4. **`Blaze.LlmGateway.Api/Program.cs`** — Existing endpoint setup (reference)
5. **`Blaze.LlmGateway.Core/**`** — LlmRoutingChatClient, existing middleware

---

## Acceptance Criteria

✅ **Endpoints Implemented:**
- [ ] `POST /v1/chat/completions` functional, streaming SSE, correct schema
- [ ] `POST /v1/completions` functional, streaming SSE, correct schema
- [ ] `GET /v1/models` functional, returns provider list

✅ **Azure SDK Integration:**
- [ ] `AzureOpenAIClient` registered as keyed service `"AzureFoundry"`
- [ ] Requests routed to Azure when appropriate
- [ ] Credential injection ready (Infra agent will provide)

✅ **Swagger/OpenAPI:**
- [ ] Swagger UI available at `/swagger/ui`
- [ ] OpenAPI schema available at `/openapi.json`
- [ ] All endpoints documented with examples

✅ **Code Quality:**
- [ ] Compiles with `-warnaserror` (zero warnings)
- [ ] MEAI-compliant (IChatClient, ChatMessage, etc.)
- [ ] ADR-0008 compliant (no unauthorized cloud-egress)
- [ ] Follows existing C# style (primary constructors, collection expressions)

✅ **Deliverables:**
- [ ] All 5 new files created and implemented
- [ ] `Program.cs` updated with endpoints + Azure SDK + Swagger
- [ ] Code ready for testing by Tester agent

---

## Inherited Assumptions

| Assumption | Status |
|-----------|--------|
| Azure OpenAI SDK available in NuGet feed | ✅ |
| Existing LlmRoutingChatClient supports new providers | ✅ |
| NSwag/Swashbuckle available for Swagger | ✅ |
| MEAI streaming patterns established | ✅ |
| Keyed DI for providers already in use | ✅ |

---

## Common Gotchas

1. **Streaming Format:** OpenAI SSE format is strict: `data: {json}\n\n` with final `data: [DONE]\n\n`
2. **Model List:** Query both Azure and existing providers (Ollama, Gemini, etc.)
3. **Azure Credentials:** Don't hardcode; await Infra agent to inject via Aspire
4. **Primary Constructors:** Use `public class ChatCompletionsEndpoint(IRequestHandler handler)` syntax
5. **CancellationToken:** Propagate through all async calls: `async (CancellationToken ct) => ...`

---

## Checklist

- [ ] Read PRD, plan, and this handoff
- [ ] Create worktree: `git worktree add -b feature/litellm-gateway-coder .worktrees/litellm-gateway-coder main`
- [ ] Switch to worktree: `git worktree list`
- [ ] Implement all 5 new files + update Program.cs
- [ ] Verify compilation: `dotnet build Blaze.LlmGateway.Api --no-incremental -warnaserror`
- [ ] Run endpoints locally (optional): `dotnet run --project Blaze.LlmGateway.Api`
- [ ] Commit changes: `git add -A && git commit -m "feat: Implement LiteLLM endpoints + Azure SDK integration"`
- [ ] Emit `[DONE]` signal when complete

---

## Success Signal

When all acceptance criteria are met, emit:

```
[DONE] Task: Coder — LiteLLM endpoints + Azure SDK integration
Status: ✅ All 3 endpoints implemented, Swagger configured, code -warnaserror clean
Artifacts: ChatCompletionsEndpoint.cs, CompletionsEndpoint.cs, ModelsEndpoint.cs, OpenAiModels.cs, Program.cs
Reason: Endpoints functional, schemas correct, ready for testing
```

---

*Handoff created by squad-orchestrator at 2026-04-20T21:43:45Z*  
*Run ID: 20260420-214331-litellm-gateway*
