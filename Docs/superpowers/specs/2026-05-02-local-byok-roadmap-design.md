# Local-BYOK CodebrewRouter — 2-Phase Roadmap Design Spec

> **Date:** 2026-05-02  
> **Status:** Design approved (rubber duck validated)  
> **Owner:** User (local environment)  
> **Goal:** Get CodebrewRouter running end-to-end with local-only models (no cloud), validated through Aspire + DevUI  

---

## Executive Summary

CodebrewRouter is being reconfigured from a **multi-cloud gateway** to a **local-only, BYOK (bring-your-own-models) system** to minimize costs while maintaining full functionality.

**Architecture:**
- **Router:** Redundant Ollama instances (192.168.16.12 and 192.168.16.53) with primary/fallback
- **Task Executor:** LM Studio (192.168.16.56)
- **Pipeline:** Prompt cleanup → Router classification → LM Studio execution
- **Validation:** All work validated through Aspire + DevUI, no external cloud dependencies

**Phases:**
1. **Phase 1 (3-5 hrs):** Local routing setup — remove cloud providers, configure redundant Ollama
2. **Phase 2 (2-3 hrs):** Validate prompt cleanup — verify existing `GemmaPromptCleaner` works with messy prompts

**Future phases documented for later planning** (vision routing, advanced context routing, premium escalation).

---

## Why This Approach?

### Current State Problems
- Gateway configured for Azure, GitHub Models, Gemini — all requiring cloud API keys
- No clear BYOK path; cost growing with each LLM call
- Prompt cleanup exists but not validated for local use
- Redundancy not configured despite having two Ollama instances available

### Solution Benefits
- ✅ **Cost:** 100% local models, zero cloud API spend
- ✅ **Control:** All data stays on your network
- ✅ **Redundancy:** Dual Ollama routers provide fallback
- ✅ **Simplicity:** One default model (LM Studio), everything else is router brain
- ✅ **Growth Path:** Proven local setup enables future premium escalation when justified (e.g., Claude for .NET MAUI)

---

## PHASE 1 — Local Routing Setup & Redundancy

### Scope
Remove all cloud provider code/config/infrastructure. Configure dual Ollama routers. Verify routing to LM Studio only.

### What Changes

**Configuration:**
- Update `FallbackRules`: all 7 task types → `["LmStudio"]` only (remove AzureFoundry, GithubModels)
- Remove cloud provider DI registrations from `InfrastructureServiceExtensions.cs`
- Configure dual Ollama: Primary @ 192.168.16.53, Fallback @ 192.168.16.12
- Remove cloud resources from Aspire AppHost

**Code:**
- Update `ModelAvailabilityHeartbeatService` to probe both Ollama instances
- Simplify `CodebrewRouterChatClient` if there's cloud-specific logic
- Clean up unused provider references

**Testing:**
- Add failover test (verify .12 is used when .53 is down)
- Verify all 3 resources appear in Aspire DevUI as healthy

### Tasks

| # | Task | File(s) | Details |
|---|---|---|---|
| 1.1 | Update FallbackRules to `["LmStudio"]` | `appsettings.json` | All 7 task types: Reasoning, Coding, Research, VisionObjectDetection, Creative, DataAnalysis, General |
| 1.2 | Remove cloud provider DI registrations | `InfrastructureServiceExtensions.cs` | Remove: `AddKeyedSingleton<IChatClient>("AzureFoundry", ...)`, `"GithubModels"`, `"FoundryLocal"` |
| 1.3 | Configure dual Ollama routers | `appsettings.json` (OllamaLocal config) | Add: Primary @ .53:11434, Fallback @ .12:11434 |
| 1.4 | Update heartbeat to probe both Ollama | `ModelAvailabilityHeartbeatService.cs` | Check both .53 and .12 reachable on startup |
| 1.5 | Remove cloud resources from AppHost | `Blaze.LlmGateway.AppHost/Program.cs` | Remove: AzureAIFoundryLocal resource, GithubModels resource; keep: Ollama, LmStudio |
| 1.6 | Clean up cloud-specific routing logic | `CodebrewRouterChatClient.cs` or other | If cloud providers have special handling, remove/simplify |
| 1.7 | Add failover test | `Blaze.LlmGateway.Tests/` | Test: stop .53, send request, verify .12 handles it |
| 1.8 | Manual validation via Aspire | Aspire dashboard | Verify 3 resources (Ollama .53, .12, LM Studio) show as Running |

### Definition of Done

**Build & Infrastructure:**
- ✅ `dotnet build --no-incremental -warnaserror` succeeds with 0 warnings
- ✅ `dotnet run --project Blaze.LlmGateway.AppHost` starts cleanly
- ✅ No errors about missing cloud credentials (Azure, GitHub, Gemini)

**Aspire + DevUI:**
- ✅ Aspire dashboard shows exactly 3 resources: OllamaLocal-Primary (.53:11434), OllamaLocal-Fallback (.12:11434), LmStudio (.56:1234)
- ✅ All 3 resources report "Running" status
- ✅ No cloud provider resources present (Azure Foundry, GitHub Models, Gemini removed)
- ✅ Logs show no cloud API connection attempts

**Routing Validation:**
- ✅ Curl test succeeds:
  ```bash
  curl -X POST http://localhost:5022/v1/chat/completions \
    -H "Content-Type: application/json" \
    -d '{"model":"codebrewRouter","messages":[{"role":"user","content":"Hello"}]}'
  ```
  Expected: 200 OK with response from LM Studio
- ✅ DevUI logs show: "routing to LmStudio" + model response
- ✅ FallbackRules verified: only `["LmStudio"]` per task type

**Redundancy:**
- ✅ Primary router (.53) handles requests normally
- ✅ Stop Ollama @ .53 → system falls back to .12 (test case verifies)
- ✅ Heartbeat detects both instances as healthy on startup
- ✅ Heartbeat detects .53 as unavailable, marks it unhealthy (continues with .12)

**Code Quality:**
- ✅ Tests pass: 248+ of 251 (3 have known integration test isolation, pass individually)
- ✅ Build succeeds: `dotnet build --no-incremental -warnaserror`
- ✅ No unused provider registrations in DI container

---

## PHASE 2 — Validate & Fine-Tune Prompt Cleanup

### Scope
Verify existing prompt cleanup (`GemmaPromptCleaner`) is configured to use local Ollama, works with user's test cases, and integrates end-to-end.

### What's Already Done
The codebase **already has prompt cleanup fully implemented:**
- ✅ `IPromptCleaner` interface defined
- ✅ `GemmaPromptCleaner` implementation exists (uses Ollama @ .12:11434, temperature=0)
- ✅ Circuit breaker with configurable cooldown (default: 5 min)
- ✅ Minimum length filter (< 80 chars skips cleanup)
- ✅ **Already wired into `CodebrewRouterChatClient.CleanMessagesAsync()`**

Phase 2 is **validation + configuration**, not implementation.

### What We're Verifying

1. **Configuration:** Cleanup uses local Ollama, not cloud model
2. **Behavior:** Messy prompts get cleaned deterministically (temperature=0)
3. **Integration:** Cleaned prompts route + execute via LM Studio correctly
4. **Pipeline:** Full flow (cleanup → classify → route → execute) works end-to-end
5. **Fallback:** Circuit breaker engages if Ollama fails

### Pipeline

```
User Request (raw, messy)
    ↓
CodebrewRouterChatClient.CleanMessagesAsync()
    └─→ IPromptCleaner (GemmaPromptCleaner)
        └─→ Ollama @ .12:11434 (gemma4:e4b, temperature=0)
    ↓
Cleaned messages
    ↓
CodebrewRouter.GetResponseAsync()
    ├─→ OllamaMetaRoutingStrategy (via .53 primary, .12 fallback)
    ↓
LM Studio @ .56:1234 (with cleaned prompt)
    ↓
Response (SSE stream)
```

### Configuration (should already be correct in appsettings.json)

```json
"PromptCleanup": {
  "Enabled": true,
  "MaxOutputTokens": 256,
  "Temperature": 0.0,
  "MinLengthChars": 80,
  "CooldownMinutes": 5
}
```

### Tasks

| # | Task | File(s) | Details |
|---|---|---|---|
| 2.1 | Verify PromptCleanupOptions.Enabled = true | `appsettings.json` | Should already be true; if not, set it |
| 2.2 | Verify GemmaPromptCleaner uses local Ollama | `GemmaPromptCleaner.cs:25-30` | Confirm `_ollama.GetResponseAsync()` endpoint is `.12:11434` |
| 2.3 | Verify circuit breaker configuration | `GemmaPromptCleaner.cs:62-104` | CooldownMinutes = 5; confirm skip logic |
| 2.4 | Manual test: clean prompt (no cleanup needed) | Curl | Input: "Tell me a dad joke" → should skip cleanup (< 80 chars), route to LM Studio |
| 2.5 | Manual test: messy prompt #1 | Curl | Input: "um, what is a dad joke that, um, makes me laugh" → cleaned, then routed |
| 2.6 | Manual test: messy prompt #2 | Curl | Input: "provide me the best code for making a calculator app for web" → cleaned, then routed |
| 2.7 | Manual test: streaming response | Curl with `--no-buffer` | Verify cleanup is transparent; response streams without delays |
| 2.8 | Test: circuit breaker activation | Manual integration test | Stop Ollama @ .12 (or simulate failure) → after 5 cleanup attempts fail, cleanup skipped until cooldown expires |
| 2.9 | Add/extend cleanup tests | `Blaze.LlmGateway.Tests/` | Unit + integration tests for messy prompts |

### Manual Test Examples

**Test 1: Clean prompt (skip cleanup)**
```bash
curl -X POST http://localhost:5022/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"codebrewRouter","messages":[{"role":"user","content":"Tell me a dad joke"}]}'

# Expected: 
# - DevUI logs: "Prompt < 80 chars, skipping cleanup"
# - Routes directly to LM Studio
# - Response stream normal
```

**Test 2: Messy prompt**
```bash
curl -X POST http://localhost:5022/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"codebrewRouter","messages":[{"role":"user","content":"um, what is a dad joke that, um, makes me laugh"}]}'

# Expected:
# - DevUI logs: "Cleaning prompt via GemmaPromptCleaner"
# - Cleaned output (e.g., "Tell me a funny dad joke")
# - Routes cleaned prompt to LM Studio
# - Response includes humor about dads
```

**Test 3: Complex messy prompt**
```bash
curl -X POST http://localhost:5022/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"codebrewRouter","messages":[{"role":"user","content":"provide me the best code for making a calculator app for web"}]}'

# Expected:
# - DevUI logs: "Cleaning prompt"
# - Cleaned deterministically (temperature=0)
# - Routes to LM Studio
# - Response: web calculator code (e.g., HTML + JS or React)
```

**Test 4: Streaming response**
```bash
curl -N -X POST http://localhost:5022/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"codebrewRouter","messages":[{"role":"user","content":"um, what is a dad joke that, um, makes me laugh"}]}'

# Expected:
# - Stream chunks arrive normally (no stalls)
# - Cleanup is transparent (user doesn't see cleanup messages)
# - [DONE] terminator sent at end
```

### Definition of Done

**Setup (Phase 1 still passing):**
- ✅ `dotnet build --no-incremental -warnaserror` succeeds
- ✅ Aspire runs, DevUI shows 3 resources healthy

**Cleanup Configuration:**
- ✅ `appsettings.json` has `PromptCleanup.Enabled: true`
- ✅ `GemmaPromptCleaner` registered in DI
- ✅ Cleanup uses **local Ollama** (.12:11434), not cloud model
- ✅ Temperature = 0.0 (deterministic)
- ✅ MinLengthChars = 80 (skip cleanup for short prompts)
- ✅ CooldownMinutes = 5 (circuit breaker)

**Messy Prompt Validation (curl tests pass):**
- ✅ "Tell me a dad joke" → skips cleanup, routes to LM Studio, returns response
- ✅ "um, what is a dad joke that, um, makes me laugh" → cleaned, routed, returns response
- ✅ "provide me the best code for making a calculator app for web" → cleaned, routed, returns response
- ✅ DevUI logs show cleanup step before routing for messy prompts
- ✅ Outputs are deterministic (same input → same cleaned output)

**Streaming:**
- ✅ Streaming response works without delays or errors
- ✅ Cleanup is transparent to client (no cleanup messages in stream)
- ✅ [DONE] terminator sent correctly

**Circuit Breaker:**
- ✅ Stop local Ollama @ .12 (or simulate network failure)
- ✅ Send request → cleanup attempts fail (5 retries or faster backoff)
- ✅ After failure threshold, cleanup skipped for CooldownMinutes (5 min)
- ✅ Request proceeds without cleanup
- ✅ After cooldown expires, cleanup re-enabled

**Code Quality:**
- ✅ Tests pass (248+/251)
- ✅ Build succeeds with `-warnaserror`
- ✅ New/extended tests for messy prompts included

---

## Future Phases (Documented for Later Scheduling)

### Phase 3: Vision Routing [Future]
**Goal:** Detect vision content (images) and route to vision-capable models.
**Scope:** Image detection + vision-capable model routing
**Timeline:** Defer until Phase 1-2 stable, user requests vision support
**Estimated effort:** 4-6 hours

### Phase 4: Advanced Context Routing [Future]
**Goal:** Query model capabilities, cache metadata, route large conversations intelligently.
**Scope:** Capability discovery, smart routing, future Claude/Gemini escalation
**Timeline:** Defer until Phase 1-2 stable + clearer requirements
**Estimated effort:** 6-8 hours
**Note:** Overlaps with existing context compaction; requires careful integration spec.

### Phase 5+: Premium Model Escalation [Future, Lower Priority]
**Goal:** When LM Studio can't handle a task, escalate to Claude/Gemini/Codex (cost-gated).
**Timeline:** After local stack proven, cost justifies premium escalation
**Estimated effort:** TBD

---

## Rollout Plan

| Phase | Duration | Effort | Owner | Start | Blocker |
|-------|----------|--------|-------|-------|---------|
| **Phase 1** | 3-5 hrs | Medium | User (or coder subagent) | Immediately | None |
| **Phase 2** | 2-3 hrs | Low | User (or tester subagent) | After Phase 1 DoD ✅ | Phase 1 DoD |
| **Phase 3** | 4-6 hrs | Medium | Future | After user requests | Phase 2 DoD ✅ |
| **Phase 4** | 6-8 hrs | High | Future | After requirements | Phase 2 DoD ✅ + spec |
| **Phase 5+** | TBD | TBD | Future | Future | Business case |

---

## Success Metrics (End of Phase 2)

- ✅ **Cost:** Zero cloud API spend (all local models)
- ✅ **Reliability:** Redundant Ollama routers with automatic failover
- ✅ **Usability:** Messy prompts cleaned automatically before execution
- ✅ **Visibility:** All components visible in Aspire + DevUI
- ✅ **Testing:** 248+/251 tests passing, build clean with `-warnaserror`
- ✅ **End-to-End:** User can chat naturally, system cleans + routes + executes automatically

---

## Assumptions

1. Ollama @ .12:11434 and .53:11434 are reachable and have `gemma4:e4b` model loaded
2. LM Studio @ .56:1234 is reachable and has models available
3. Aspire orchestration is the primary development validation method
4. `GemmaPromptCleaner` implementation is correct (rubber duck validated)
5. FallbackRules is the actual routing config (not the unused `RouteDestination` enum)

---

## Known Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Ollama .53 or .12 slow/unresponsive | Delays in routing | Redundancy + heartbeat monitoring; timeouts configured |
| Prompt cleanup fails (Ollama down) | Circuit breaker kicks in | Built-in circuit breaker (5 min cooldown); cleanup is optional |
| Large conversation exceeds LM Studio budget | Request fails | Existing context compaction handles this (Phase 1 includes it) |
| Phase 1 removes cloud providers, but code references them | Build fails | Systematic grep for provider references; remove all keyed DI registrations |
| AppHost still wires cloud resources | Aspire fails | Remove Azure Foundry Local + GitHub Models from AppHost `Program.cs` |

---

## Appendix: Configuration Reference

### appsettings.json (Target State)

```json
{
  "LlmGateway": {
    "Providers": {
      "OllamaLocal": {
        "BaseUrl": "http://192.168.16.12:11434",
        "Model": "gemma4:e4b",
        "MaxContextTokens": 32768
      },
      "OllamaRouter": {
        "Primary": "http://192.168.16.53:11434",
        "Secondary": "http://192.168.16.12:11434",
        "Model": "gemma4:e4b"
      },
      "LmStudio": {
        "Endpoint": "http://192.168.16.56:1234/v1",
        "Model": "local-model",
        "MaxContextTokens": 8192
      }
    },
    "CodebrewRouter": {
      "Enabled": true,
      "ModelId": "codebrewRouter",
      "FallbackRules": {
        "Reasoning": ["LmStudio"],
        "Coding": ["LmStudio"],
        "Research": ["LmStudio"],
        "VisionObjectDetection": ["LmStudio"],
        "Creative": ["LmStudio"],
        "DataAnalysis": ["LmStudio"],
        "General": ["LmStudio"]
      }
    },
    "PromptCleanup": {
      "Enabled": true,
      "MaxOutputTokens": 256,
      "Temperature": 0.0,
      "MinLengthChars": 80,
      "CooldownMinutes": 5
    }
  }
}
```

---

**Document version:** 1.0 (approved by rubber duck 2026-05-02)  
**Next action:** Transition to Phase 1 implementation planning
