# Product Requirements Document: LiteLLM-Compatible Gateway

**Version:** 1.0  
**Date:** 2026-04-20  
**Author:** Copilot CLI  
**Status:** Ready for Orchestrator

---

## Executive Summary

Transform Blaze.LlmGateway into a **LiteLLM-compatible proxy gateway** that exposes OpenAI-compatible endpoints matching LiteLLM's API surface. Integrate **Azure OpenAI SDK** as the first production-ready model provider, expose full **OpenAPI/Swagger documentation**, and achieve **95% test coverage** on new code.

This effort positions the gateway as a drop-in replacement for LiteLLM in .NET environments while leveraging the existing MEAI routing and provider infrastructure.

---

## Problem Statement

Currently, Blaze.LlmGateway:
- Exposes only a single custom `/v1/chat/completions` endpoint
- Lacks model discovery endpoints (`/v1/models`)
- Has no legacy text-completion endpoint (`/v1/completions`)
- Provides no OpenAPI/Swagger documentation
- Has minimal test coverage (unknown exact %)
- Lacks production-grade Azure OpenAI SDK integration

This limits adoption by teams expecting a **fully LiteLLM-compatible API surface**.

---

## Goals

1. **Endpoint Compatibility** — Implement all core LiteLLM-compatible endpoints with correct request/response schemas
2. **Provider Integration** — Integrate Azure OpenAI SDK as the first fully-supported model provider
3. **Documentation** — Expose OpenAPI/Swagger schema; provide interactive API docs
4. **Test Coverage** — Achieve 95% coverage on new endpoint code; maintain >80% overall
5. **Production Readiness** — Ensure clean build with `-warnaserror`, all tests passing, Aspire orchestration working

---

## Scope

### In Scope ✅
- `POST /v1/chat/completions` — OpenAI-compatible chat endpoint (enhance existing)
- `POST /v1/completions` — Legacy text-only completions
- `GET /v1/models` — List available providers and configured models
- OpenAPI/Swagger schema generation and UI
- Azure OpenAI SDK integration via keyed DI
- Unit tests for all endpoints
- Integration tests for LiteLLM compatibility
- AppHost configuration for Azure SDK credentials
- Service defaults for Swagger/OpenAPI discovery

### Out of Scope ❌
- Embeddings endpoint (`POST /v1/embeddings`)
- Vision/multimodal endpoints
- Audio/speech endpoints
- Fine-tuning endpoints
- Admin/management endpoints
- Rate limiting / cost tracking (future work)

---

## Acceptance Criteria

### Endpoints
- [ ] `POST /v1/chat/completions` accepts standard OpenAI chat request, streams SSE response
- [ ] `POST /v1/completions` accepts legacy text prompt, streams SSE text completions
- [ ] `GET /v1/models` returns JSON array of available providers and models
- [ ] All endpoints validate input; return appropriate HTTP errors (400, 401, 500)
- [ ] Streaming responses include OpenAI-compatible chunk format + `data: [DONE]` terminator

### Documentation
- [ ] `/swagger/ui` available and displays all endpoints with request/response schemas
- [ ] `/openapi.json` serves valid OpenAPI 3.0 schema
- [ ] Endpoint descriptions include provider routing hints

### Azure Integration
- [ ] Azure OpenAI SDK (`Azure.AI.OpenAI`) integrated as keyed provider
- [ ] AppHost injects Azure endpoint URL and API key via Aspire parameters
- [ ] Provider selection logic routes requests to Azure when appropriate
- [ ] Model availability detected and exposed via `/v1/models`

### Tests
- [ ] Unit tests for `ChatCompletionsEndpoint`, `CompletionsEndpoint`, `ModelsEndpoint`
- [ ] Integration tests calling endpoints via HttpClient
- [ ] LiteLLM compatibility tests validating request/response format
- [ ] Azure provider tests (credential handling, model availability)
- [ ] Coverage report: **95% on new endpoint code**, **>80% overall**
- [ ] All tests pass with zero failures

### Build & Quality
- [ ] `dotnet build --no-incremental -warnaserror` succeeds with zero warnings
- [ ] `dotnet test --no-build --collect:"XPlat Code Coverage"` passes with coverage report
- [ ] No new security issues (ADR-0008 cloud-egress check passes)

### Aspire Orchestration
- [ ] AppHost correctly wires Azure SDK endpoint + credentials
- [ ] Aspire dashboard displays all configured resources
- [ ] `dotnet run --project Blaze.LlmGateway.AppHost` starts without errors
- [ ] API project accessible at `http://localhost:5000` with endpoints live

---

## Technical Design

### Endpoint Contracts

#### POST /v1/chat/completions
**Request:**
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

**Response (streaming):**
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

**Response (streaming):**
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

### Architecture

**Middleware Stack:**
1. `McpToolDelegatingClient` — Injects MCP tools
2. `LlmRoutingChatClient` — Routes to target provider via `IRoutingStrategy`
3. `FunctionInvokingChatClient` — Handles tool invocation (per-provider)
4. **Keyed Provider Client** — Azure SDK, Ollama, Gemini, etc.

**New Files:**
- `Blaze.LlmGateway.Api/Endpoints/ChatCompletionsEndpoint.cs` — Refactored endpoint
- `Blaze.LlmGateway.Api/Endpoints/ModelsEndpoint.cs` — Model discovery
- `Blaze.LlmGateway.Api/Endpoints/CompletionsEndpoint.cs` — Legacy completions
- `Blaze.LlmGateway.Api/Models/OpenAiModels.cs` — DTOs for request/response
- `Blaze.LlmGateway.Tests/Endpoints/*Tests.cs` — Comprehensive endpoint tests

**Modified Files:**
- `Blaze.LlmGateway.Api/Program.cs` — Add Swagger, new endpoints, Azure SDK wiring
- `Blaze.LlmGateway.AppHost/Program.cs` — Configure Azure endpoint + API key injection
- `Blaze.LlmGateway.ServiceDefaults/Extensions.cs` — Add OpenAPI/Swagger defaults

### Provider Integration: Azure OpenAI SDK

**Why Azure OpenAI SDK?**
- Already partially wired in Aspire
- Production-grade SDKs with resilience built-in
- Native support for MEAI via adapters
- Direct Azure Foundry integration

**Implementation:**
1. Register `Azure.AI.OpenAI.AzureOpenAIClient` as keyed service `"AzureFoundry"`
2. Wrap with `.AsBuilder().UseFunctionInvocation().Build()`
3. Inject endpoint URL + API key from Aspire parameters
4. Detect available models from Azure deployment list

---

## Testing Strategy

### Unit Tests (Endpoints)
- Request validation (missing fields, invalid types)
- Response shape correctness
- Error handling (400, 401, 500 scenarios)
- Streaming format validation

### Integration Tests
- End-to-end HTTP calls via `HttpClient`
- LiteLLM compatibility (request format → response format)
- Provider routing (prompt hints → correct provider selected)
- Model availability detection

### Provider Tests
- Azure SDK credential injection
- Model listing and availability
- Fallback behavior on credential failure

### Coverage
- Target: **95% on new endpoint code**
- Maintain: **>80% on overall solution**
- Use `XPlat Code Coverage` with coverage report

---

## Deliverables

### Phase 1: PRD Generation (Orchestrator)
- ✅ This document (generated)

### Phase 2: Parallel Development (3 Agents, 3 Worktrees)

**Agent: Coder** (worktree: `.worktrees/litellm-gateway-coder/`)
- [ ] Implement 3 LiteLLM endpoints with correct schemas
- [ ] Integrate Azure OpenAI SDK as first provider
- [ ] Configure NSwag/Swashbuckle for Swagger
- [ ] Update `Program.cs` with Swagger middleware + endpoint registration

**Agent: Tester** (worktree: `.worktrees/litellm-gateway-tester/`)
- [ ] Unit tests for all 3 endpoints (input validation, output shape)
- [ ] Integration tests calling endpoints via HttpClient
- [ ] LiteLLM compatibility tests (request/response format)
- [ ] Azure provider tests (credential, model availability)
- [ ] Coverage report: 95% on new code, >80% overall

**Agent: Infra** (worktree: `.worktrees/litellm-gateway-infra/`)
- [ ] Update `AppHost/Program.cs` to inject Azure endpoint + API key
- [ ] Add Swagger/OpenAPI service defaults to `ServiceDefaults`
- [ ] Verify Aspire resource orchestration
- [ ] Document AppHost configuration changes

### Phase 3: Integration & Quality Gate (Orchestrator)
- [ ] Merge all 3 worktrees
- [ ] Run: `dotnet build --no-incremental -warnaserror`
- [ ] Run: `dotnet test --no-build --collect:"XPlat Code Coverage"`
- [ ] Verify coverage meets gates
- [ ] Verify all tests pass

---

## Success Metrics

| Metric | Target | Status |
|--------|--------|--------|
| Endpoints functional | 3/3 (chat, completions, models) | ⏳ |
| Swagger docs available | ✅ `/swagger/ui` + `/openapi.json` | ⏳ |
| Azure SDK integrated | ✅ Keyed DI + Aspire wiring | ⏳ |
| Test coverage (new code) | 95% | ⏳ |
| Test coverage (overall) | >80% | ⏳ |
| Build (no warnings) | ✅ `-warnaserror` passes | ⏳ |
| All tests passing | ✅ 100% pass rate | ⏳ |
| Aspire orchestration | ✅ Starts without errors | ⏳ |

---

## Timeline

- **Phase 1 (PRD):** ✅ Done
- **Phase 2 (Parallel Dev):** 15–20 minutes (Coder, Tester, Infra in parallel)
- **Phase 3 (Integration + QG):** 5–10 minutes
- **Total:** ~20–30 minutes (fully autonomous, no human waits)

---

## Assumptions & Dependencies

| Assumption | Status |
|-----------|--------|
| Azure OpenAI SDK is available in current NuGet feed | ✅ |
| Aspire AppHost can inject environment variables | ✅ |
| Existing routing infrastructure supports new providers | ✅ |
| NSwag/Swashbuckle can be added to Api project | ✅ |
| Test suite uses xUnit + Moq (existing convention) | ✅ |

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Azure SDK breaking changes | Use latest stable; test in CI |
| Swagger bloat on small API | Use selective endpoint documentation |
| Coverage tools incompatibility | Use `XPlat Code Coverage` (proven in Tests project) |
| Parallel worktree merge conflicts | File-lock enforcement; clear task boundaries |

---

## Notes

- All new code must follow existing C# style (primary constructors, collection expressions, CancellationToken propagation).
- Keeper guardrails: MEAI Law, keyed DI, streaming-first, cloud-egress default-deny.
- Orchestrator will use 3 isolated Git worktrees for parallel execution with automatic merge on completion.
- Security review (ADR-0008) will automatically scan diffs for cloud-egress violations.

---

## Approval & Sign-Off

- **PRD Created:** 2026-04-20
- **Approved for Orchestrator:** ✅ User approved plan
- **Status:** Ready to launch autonomous parallel development
