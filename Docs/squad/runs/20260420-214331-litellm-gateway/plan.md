# Plan — LiteLLM-Compatible Gateway

**Orchestrator:** squad-orchestrator  
**PRD:** Docs/PRD/litellm-compatible-gateway.md  
**Run ID:** 20260420-214331-litellm-gateway  
**Status:** Parallel Phase 1 — Decomposition Complete

---

## Overview

This plan decomposes the LiteLLM-Compatible Gateway PRD into 3 parallel, file-disjoint tasks:

1. **Coder Task:** Implement LiteLLM endpoints + Azure SDK integration
2. **Tester Task:** Write comprehensive unit + integration tests
3. **Infra Task:** Update AppHost and ServiceDefaults for Azure + Swagger

Each task runs in an isolated Git worktree with explicit file-locks to prevent conflicts.

---

## Phase 1: Parallel Development (3 Worktrees)

### Task 1.1: Coder — Endpoint Implementation & Azure SDK Integration

**Worktree:** `.worktrees/litellm-gateway-coder`  
**Duration:** ~10 minutes  
**Files You May Edit (Exclusive Lock):**
- `Blaze.LlmGateway.Api/Program.cs` — Add Swagger, endpoint registration, Azure SDK wiring
- `Blaze.LlmGateway.Api/Endpoints/ChatCompletionsEndpoint.cs` (create) — LiteLLM-compatible chat
- `Blaze.LlmGateway.Api/Endpoints/CompletionsEndpoint.cs` (create) — Legacy text completions
- `Blaze.LlmGateway.Api/Endpoints/ModelsEndpoint.cs` (create) — Model discovery
- `Blaze.LlmGateway.Api/Models/OpenAiModels.cs` (create) — Request/response DTOs
- `.worktrees/litellm-gateway-coder/**` (worktree-specific)

**Files You Must NOT Edit:**
- `Blaze.LlmGateway.Tests/**` (owned by Tester)
- `Blaze.LlmGateway.AppHost/**` (owned by Infra)
- `Blaze.LlmGateway.ServiceDefaults/**` (owned by Infra)

**Acceptance Criteria:**
- [ ] All 3 endpoints implemented with correct OpenAI-compatible schemas
- [ ] Streaming responses in SSE format with `data: [DONE]` terminator
- [ ] Azure OpenAI SDK registered as keyed service `"AzureFoundry"`
- [ ] Swagger/NSwag configured in `Program.cs`
- [ ] Code compiles with `-warnaserror` (no warnings)
- [ ] No new cloud-egress violations (ADR-0008 compliant)

**Deliverables:**
- ✅ ChatCompletionsEndpoint.cs (POST /v1/chat/completions)
- ✅ CompletionsEndpoint.cs (POST /v1/completions)
- ✅ ModelsEndpoint.cs (GET /v1/models)
- ✅ OpenAiModels.cs (DTOs)
- ✅ Program.cs updated with endpoints + Azure SDK + Swagger

---

### Task 1.2: Tester — Unit & Integration Tests

**Worktree:** `.worktrees/litellm-gateway-tester`  
**Duration:** ~10 minutes  
**Files You May Edit (Exclusive Lock):**
- `Blaze.LlmGateway.Tests/Endpoints/ChatCompletionsEndpointTests.cs` (create)
- `Blaze.LlmGateway.Tests/Endpoints/CompletionsEndpointTests.cs` (create)
- `Blaze.LlmGateway.Tests/Endpoints/ModelsEndpointTests.cs` (create)
- `Blaze.LlmGateway.Tests/Integration/LiteLlmCompatibilityTests.cs` (create)
- `Blaze.LlmGateway.Tests/Providers/AzureProviderTests.cs` (create)
- `.worktrees/litellm-gateway-tester/**` (worktree-specific)

**Files You Must NOT Edit:**
- `Blaze.LlmGateway.Api/**` (owned by Coder)
- `Blaze.LlmGateway.AppHost/**` (owned by Infra)
- `Blaze.LlmGateway.ServiceDefaults/**` (owned by Infra)

**Acceptance Criteria:**
- [ ] Unit tests for all 3 endpoints (input validation, output shape, error cases)
- [ ] Integration tests via HttpClient (LiteLLM compatibility)
- [ ] Azure provider tests (credential handling, model availability)
- [ ] Code coverage: **95% on new endpoint code**, **>80% overall**
- [ ] All tests pass (0 failures)
- [ ] Coverage report generated with `XPlat Code Coverage`

**Deliverables:**
- ✅ ChatCompletionsEndpointTests.cs (unit tests for chat endpoint)
- ✅ CompletionsEndpointTests.cs (unit tests for completions)
- ✅ ModelsEndpointTests.cs (unit tests for model discovery)
- ✅ LiteLlmCompatibilityTests.cs (integration tests)
- ✅ AzureProviderTests.cs (Azure SDK credential/model tests)
- ✅ Coverage report (95% new code, >80% overall)

---

### Task 1.3: Infra — AppHost & ServiceDefaults Updates

**Worktree:** `.worktrees/litellm-gateway-infra`  
**Duration:** ~5 minutes  
**Files You May Edit (Exclusive Lock):**
- `Blaze.LlmGateway.AppHost/Program.cs` — Azure endpoint + API key injection
- `Blaze.LlmGateway.ServiceDefaults/Extensions.cs` — Swagger/OpenAPI defaults
- `.worktrees/litellm-gateway-infra/**` (worktree-specific)

**Files You Must NOT Edit:**
- `Blaze.LlmGateway.Api/**` (owned by Coder)
- `Blaze.LlmGateway.Tests/**` (owned by Tester)

**Acceptance Criteria:**
- [ ] AppHost injects Azure endpoint URL from Aspire parameters
- [ ] AppHost injects Azure API key from Aspire parameters
- [ ] ServiceDefaults adds Swagger/OpenAPI service defaults
- [ ] `dotnet run --project Blaze.LlmGateway.AppHost` starts without errors
- [ ] API accessible at `http://localhost:5000` with live endpoints
- [ ] Aspire dashboard displays all configured resources

**Deliverables:**
- ✅ AppHost/Program.cs updated with Azure SDK wiring + parameter injection
- ✅ ServiceDefaults/Extensions.cs updated with Swagger service defaults
- ✅ All Aspire resources configured and accessible

---

## Phase 2: Integration & Quality Gate (Orchestrator)

**Duration:** ~5–10 minutes

### Step 2.1: Merge All Worktrees

```powershell
git checkout main
git merge feature/litellm-gateway-coder
git merge feature/litellm-gateway-tester
git merge feature/litellm-gateway-infra
```

**Success Criteria:**
- ✅ All 3 branches merged without conflicts (file-lock disjointness enforced)
- ✅ No merge conflicts

### Step 2.2: Quality Gates

#### Build Gate
```powershell
dotnet build --no-incremental -warnaserror
```

**Success Criteria:**
- ✅ Build succeeds with zero warnings
- ✅ No `-warnaserror` violations

#### Test Gate
```powershell
dotnet test --no-build --collect:"XPlat Code Coverage"
```

**Success Criteria:**
- ✅ All tests pass (0 failures)
- ✅ Coverage: 95% on new endpoint code
- ✅ Coverage: >80% overall solution

#### Security Gate (ADR-0008)
- ✅ No new cloud-egress violations
- ✅ No unapproved cloud provider additions
- ✅ Automated via Squad Security Review agent

---

## Phase 3: Clean-Context Review

**Trigger:** All agents report `[DONE]` and all quality gates pass

- **Squad Reviewer:** Audits code quality, design, MEAI compliance
- **Squad Security Review:** Validates ADR-0008 default-deny cloud-egress policy

---

## Assumptions

| Assumption | Status |
|-----------|--------|
| Azure OpenAI SDK available in NuGet feed | ✅ |
| Aspire can inject environment variables | ✅ |
| xUnit + Moq available for tests | ✅ |
| NSwag/Swashbuckle available for Swagger | ✅ |
| Git worktrees supported on Windows | ✅ |

---

## File-Lock Disjointness Matrix

| File | Coder | Tester | Infra |
|------|-------|--------|-------|
| `Api/Program.cs` | ✅ | ❌ | ❌ |
| `Api/Endpoints/*` | ✅ | ❌ | ❌ |
| `Api/Models/*` | ✅ | ❌ | ❌ |
| `Tests/Endpoints/*` | ❌ | ✅ | ❌ |
| `Tests/Integration/*` | ❌ | ✅ | ❌ |
| `Tests/Providers/*` | ❌ | ✅ | ❌ |
| `AppHost/Program.cs` | ❌ | ❌ | ✅ |
| `ServiceDefaults/Extensions.cs` | ❌ | ❌ | ✅ |

---

## Progress Tracking

### Task 1.1: Coder
- [ ] Dispatch agent (coder subagent 1)
- [ ] Worktree created: `.worktrees/litellm-gateway-coder`
- [ ] All endpoints implemented
- [ ] Code compiles (`-warnaserror`)
- [ ] `[DONE]` signal received
- [ ] Branch merged to main

### Task 1.2: Tester
- [ ] Dispatch agent (tester subagent 1)
- [ ] Worktree created: `.worktrees/litellm-gateway-tester`
- [ ] All tests written
- [ ] Coverage report: 95% new, >80% overall
- [ ] `[DONE]` signal received
- [ ] Branch merged to main

### Task 1.3: Infra
- [ ] Dispatch agent (infra subagent 1)
- [ ] Worktree created: `.worktrees/litellm-gateway-infra`
- [ ] AppHost + ServiceDefaults updated
- [ ] Aspire orchestration working
- [ ] `[DONE]` signal received
- [ ] Branch merged to main

### Quality Gates
- [ ] Build gate passes (`-warnaserror`)
- [ ] Test gate passes (0 failures, 95% coverage)
- [ ] Security gate passes (ADR-0008 compliant)

### Clean-Context Review
- [ ] Squad Reviewer audit complete
- [ ] Squad Security Review complete
- [ ] Final sign-off

---

## Execution Timeline

| Phase | Duration | Owner |
|-------|----------|-------|
| Phase 1.1 (Coder) | ~10 min | Coder subagent |
| Phase 1.2 (Tester) | ~10 min | Tester subagent |
| Phase 1.3 (Infra) | ~5 min | Infra subagent |
| Phase 2 (Integration + QG) | ~10 min | Orchestrator |
| Phase 3 (Review) | ~10 min | Squad Reviewer + Security |
| **Total** | **~45 min** | **Autonomous** |

---

## Success Criteria (High Level)

✅ **Endpoints:** 3/3 functional (chat, completions, models)  
✅ **Documentation:** Swagger UI + OpenAPI JSON available  
✅ **Azure Integration:** SDK wired via keyed DI + Aspire  
✅ **Test Coverage:** 95% new code, >80% overall  
✅ **Build:** Zero warnings (`-warnaserror`)  
✅ **Tests:** 100% pass rate  
✅ **Aspire:** Orchestration working without errors  

---

## Notes

- **Isolation:** Each agent runs in a separate worktree with exclusive file-locks; no conflicts expected.
- **Parallel Execution:** Coder, Tester, and Infra run simultaneously; no sequencing required.
- **Merge Strategy:** All branches merged to main only after agents complete; orchestrator handles merges.
- **Quality Assurance:** Build gate + test gate + security gate enforced before final sign-off.
- **Clean-Context Review:** All decisions logged to `reasoning.log.md` for audit trail.

---

*Generated by squad-orchestrator*  
*Plan Status: Ready for Phase 1 Execution*
