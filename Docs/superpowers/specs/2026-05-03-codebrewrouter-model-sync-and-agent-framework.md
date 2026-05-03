# CodebrewRouter: Model Sync + Agent Framework Integration Design

> **Date:** 2026-05-03  
> **Status:** Design (Brainstorming Phase)  
> **Owner:** User + Copilot  
> **Goal:** Align local model architecture with Agent Framework integration for Phase 2 launch  

---

## Executive Summary

CodebrewRouter is being configured as a **local-only, Agent Framework-ready gateway** that handles prompt optimization, context-aware routing, and intelligent task classification. This design spec establishes:

1. **Model Architecture** — Three-tier system with synchronized routers and a single worker
2. **Request Pipeline** — 5-step flow (cleanup → count → classify → route → execute)
3. **Agent Framework Integration** — How Agent Framework invokes CodebrewRouter for multi-step reasoning
4. **Redundancy & Validation** — Model sync checks and dynamic failover

---

## Architecture Overview

### Three-Tier System

```
┌─────────────────────────────────────────────────────────────┐
│ OPEN WEBUI + Agent Framework (future)                        │
│ └─→ POST /v1/chat/completions                               │
└─────────────────────────────────────────────────────────────┘
                      ↓
┌─────────────────────────────────────────────────────────────┐
│ CodebrewRouter (C# Gateway)                                  │
│ ├─ Step 1: Prompt Cleanup (gemma4:e4b @ .12)               │
│ ├─ Step 2: Context Counting (token count)                  │
│ ├─ Step 3: Task Classification (Coding/Vision/Reasoning)   │
│ ├─ Step 4: Routing Decision (lookup FallbackRules)         │
│ └─ Step 5: Provider Execution (with first-chunk failover)  │
└─────────────────────────────────────────────────────────────┘
         ↙                    ↓                    ↖
    .12 (Primary)        .56 (Worker)         .53 (Backup)
    Router                LM Studio            Router
    Ollama                qwen/qwen3.6-27b     Ollama
    gemma4:e4b            (vision+code+MoE)    gemma4:e4b
    9 GB                  Already loaded       9 GB
    Primary              (Inference engine)    (Failover)
```

### Model Selection

| Component | Model | Size | Purpose |
|-----------|-------|------|---------|
| **.12 (Primary Router)** | `gemma4:e4b` | 9 GB | Prompt cleanup + task classification |
| **.53 (Backup Router)** | `gemma4:e4b` | 9 GB | Identical to .12 for failover reliability |
| **.56 (Worker)** | `qwen/qwen3.6-27b` | ~27B params | Inference execution (vision + code + reasoning + MoE) |

**Why these models?**
- **gemma4:e4b**: Vision-capable, lightweight, deterministic reasoning (temp=0)
- **qwen3.6-27b**: Already installed, has vision, MoE handles diverse tasks, proven stable
- **Synchronized .12/.53**: Ensures failover doesn't drop capability; both have identical model capability

---

## Request Pipeline (5 Steps)

```
User Prompt (raw, possibly messy)
    ↓ STEP 1: Prompt Cleanup
    ├─ Circuit breaker check (skip if recently failed)
    ├─ Length check (skip if < 80 chars)
    └─ Ollama .12 @ gemma4:e4b, temp=0 → optimized prompt
    ↓ STEP 2: Context Counting
    └─ Token count of cleaned messages
    ↓ STEP 3: Task Classification
    ├─ Analyze cleaned messages
    └─ Determine TaskType: Coding | Reasoning | VisionObjectDetection | Research | Creative | DataAnalysis | General
    ↓ STEP 4: Routing Decision
    ├─ Look up CodebrewRouterOptions.FallbackRules[TaskType]
    └─ Get provider chain: e.g., ["LmStudio"] for all task types
    ↓ STEP 5: Provider Execution
    ├─ For each provider in chain:
    │  ├─ Send cleaned messages
    │  ├─ Get first chunk response
    │  ├─ On success: stream rest of response
    │  └─ On failure: try next provider
    └─ If all fail: use InnerClient fallback (.56 as last resort)
    ↓
Response (SSE streaming)
```

### Why This Order?
1. **Cleanup first** — Removes noise, improves classification accuracy
2. **Count early** — Token budget influences routing decisions (future enhancement)
3. **Classify after cleanup** — Cleaned text gives more accurate task detection
4. **Route based on task** — Different task types may route to different models (future)
5. **Execute last** — .56 receives optimized, classified messages

---

## Model Synchronization Strategy

### .12 and .53 Must Be Identical

**Why?** If `.12` fails and `.53` takes over, clients expect identical routing decisions and model availability.

### Validation on Startup

```csharp
// Pseudocode for startup validation
var models12 = await QueryModels("http://192.168.16.12:11434/api/tags");
var models53 = await QueryModels("http://192.168.16.53:11434/api/tags");

var diff = models12.Except(models53).Union(models53.Except(models12));
if (diff.Any())
{
    logger.LogError("⚠️ Model mismatch between .12 and .53: {Diff}", diff);
    // Fail startup if in production, warn in dev
}
```

### Installation Commands (Phase 1 Setup)

**On .12 (already has it):**
```bash
# Already present
ollama list  # Should show: gemma4:e4b 8.95GB
```

**On .53 (install if missing):**
```bash
# SSH or direct access to .53
ssh 192.168.16.53
ollama pull gemma4:e4b
ollama list  # Verify: gemma4:e4b 8.95GB
```

**Verification:**
```bash
# From gateway machine or DevUI
curl http://192.168.16.12:11434/api/tags | jq '.models[].name'
curl http://192.168.16.53:11434/api/tags | jq '.models[].name'
# Both should show: gemma4:e4b
```

---

## Dynamic Failover (.12 → .53)

### Scenario: .12 Becomes Unavailable

1. **On first request:** Gateway tries .12, TCP timeout or connection refused
2. **First-chunk probe fails** — Mark .12 as "unhealthy"
3. **Fallback to .53** — Retry request with .53 endpoint
4. **Background health check** — Periodically probe .12 (every 5 minutes)
5. **Recovery** — Once .12 responds, switch back

### Implementation Points

- `OllamaMetaRoutingStrategy` or new `OllamaRouterHealthCheck` class
- Singleton state: `_primaryRouterHealthy` flag + `_lastFailureTime`
- Thread-safe access (use `lock` or `ReaderWriterLockSlim`)
- Configuration: failover cooldown (default 5 min), probe interval

---

## Agent Framework Integration

### How Agent Framework Uses CodebrewRouter

**Agent Framework** (Phase 3+) will invoke CodebrewRouter per reasoning step:

```csharp
// Pseudocode: Agent Framework calling CodebrewRouter
while (agentRunning)
{
    var step = agent.Think(currentContext);
    
    if (step.RequiresToolCall)
    {
        // Call CodebrewRouter for tool selection
        var toolResponse = await httpClient.Post("/v1/chat/completions", new {
            model = "codebrewRouter",
            messages = step.Messages,
            tools = availableTools
        });
        
        agent.ProcessToolResponse(toolResponse);
    }
    else
    {
        // Call CodebrewRouter for reasoning
        var reasoningResponse = await httpClient.Post("/v1/chat/completions", new {
            model = "codebrewRouter",
            messages = step.Messages
        });
        
        agent.ProcessResponse(reasoningResponse);
    }
}
```

### CodebrewRouter Role
- **Receives:** Multi-turn conversation + optional tools
- **Does:** Cleanup → Classify → Route → Execute
- **Returns:** Optimized response (text or tool invocation)
- **Benefit:** Every agent step gets efficient, routed execution

---

## Configuration Changes

### appsettings.json

```json
{
  "LlmGateway": {
    "Providers": {
      "OllamaRouter": {
        "Endpoint": "http://192.168.16.12:11434",
        "Model": "gemma4:e4b"
      },
      "OllamaRouterBackup": {
        "Endpoint": "http://192.168.16.53:11434",
        "Model": "gemma4:e4b"
      },
      "LmStudio": {
        "Endpoint": "http://192.168.16.56:1234/v1",
        "Model": "qwen/qwen3.6-27b"
      }
    },
    "CodebrewRouter": {
      "Enabled": true,
      "FallbackRules": {
        "Reasoning": ["LmStudio"],
        "Coding": ["LmStudio"],
        "Research": ["LmStudio"],
        "VisionObjectDetection": ["LmStudio"],
        "Creative": ["LmStudio"],
        "DataAnalysis": ["LmStudio"],
        "General": ["LmStudio"]
      },
      "PromptCleanup": {
        "Enabled": true,
        "CooldownMinutes": 5,
        "MinLengthChars": 80
      }
    }
  }
}
```

---

## Success Criteria

### Phase 1 (Setup)
- ✅ `.12` has `gemma4:e4b` (already done)
- ✅ `.53` has `gemma4:e4b` (pull + verify)
- ✅ Startup validation passes (model lists match)
- ✅ Dynamic failover logic wired
- ✅ Configuration updated

### Phase 2 (Testing)
- ✅ Open WebUI chat works (cleanup + classify + route + execute)
- ✅ Prompt cleanup produces valid outputs
- ✅ Token counting works
- ✅ Task classification is accurate
- ✅ Failover: Stop .12, requests go to .53, then back to .12 when healthy

### Phase 3+ (Agent Framework)
- ✅ Agent Framework can call `/v1/chat/completions` repeatedly
- ✅ Each call gets proper cleanup + routing
- ✅ Tools are forwarded and executed
- ✅ Multi-step reasoning loops work

---

## Known Limitations & Future Work

| Item | Current | Future |
|------|---------|--------|
| **Vision routing** | Routers have vision, workers have vision | Separate vision-optimized model chain |
| **Context-aware routing** | Token count tracked, not used yet | Use token budget to select model tier |
| **Cloud escalation** | Not present | Route complex tasks to cloud providers |
| **MCP integration** | Commented out | Enable tool discovery + execution |
| **Agent Framework** | Not integrated | Phase 3: Full multi-step reasoning |

---

## Implementation Checklist

- [ ] Pull `gemma4:e4b` on .53
- [ ] Verify model sync (startup validation)
- [ ] Wire dynamic failover in OllamaMetaRoutingStrategy
- [ ] Update appsettings.json
- [ ] Update DI registrations (remove cloud providers, add .53)
- [ ] Add integration tests for failover
- [ ] Test Open WebUI end-to-end
- [ ] Document setup steps for reproducibility

---

## References

- **CodebrewRouter implementation:** `Blaze.LlmGateway.Infrastructure/CodebrewRouterChatClient.cs`
- **Prompt cleanup:** `Blaze.LlmGateway.Infrastructure/PromptCleaning/GemmaPromptCleaner.cs`
- **Task classification:** `Blaze.LlmGateway.Infrastructure/TaskClassification/`
- **Configuration:** `Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs`
- **Pipeline:** `analysis.md` (Part 1–3)
