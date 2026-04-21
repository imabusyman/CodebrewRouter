---
from: squad-orchestrator
to: Squad Tester
task: testing.litellm-gateway-endpoints
phase: Phase 1.2 (Parallel)
worktree: .worktrees/litellm-gateway-tester
branch: feature/litellm-gateway-tester
run_id: 20260420-214331-litellm-gateway
---

# Handoff: Tester — LiteLLM-Compatible Gateway Test Suite

**Orchestrator:** squad-orchestrator  
**Agent:** Squad Tester subagent 1  
**Task:** Write comprehensive unit + integration tests with 95% coverage target  
**Worktree:** `.worktrees/litellm-gateway-tester`  
**Branch:** `feature/litellm-gateway-tester`  
**Duration:** ~10 minutes  

---

## Scope

You are responsible for **test coverage of all 3 new endpoints** and **Azure provider integration**. Your tests are isolated to the `Tests` project and a dedicated worktree branch.

### Files You May Edit (Exclusive Lock)

You have **exclusive edit rights** to these test files:

1. **`Blaze.LlmGateway.Tests/Endpoints/ChatCompletionsEndpointTests.cs`** (create)
   - Unit tests for `POST /v1/chat/completions`
   - Request validation (missing fields, invalid types, edge cases)
   - Response shape validation (choices, delta, content)
   - Streaming format validation (SSE, `[DONE]` terminator)
   - Error cases (400, 401, 500 scenarios)
   - Mock LlmRoutingChatClient for deterministic testing

2. **`Blaze.LlmGateway.Tests/Endpoints/CompletionsEndpointTests.cs`** (create)
   - Unit tests for `POST /v1/completions`
   - Request validation (missing prompt, invalid types)
   - Response shape validation (choices, text)
   - Streaming format validation
   - Error cases

3. **`Blaze.LlmGateway.Tests/Endpoints/ModelsEndpointTests.cs`** (create)
   - Unit tests for `GET /v1/models`
   - Returns valid model list
   - Model objects have correct structure (id, object, provider)
   - Model discovery from Azure + existing providers

4. **`Blaze.LlmGateway.Tests/Integration/LiteLlmCompatibilityTests.cs`** (create)
   - Integration tests via `HttpClient`
   - Full request/response cycle
   - LiteLLM compatibility (request format → response format)
   - Provider routing (prompt hints → correct provider selected)
   - End-to-end streaming

5. **`Blaze.LlmGateway.Tests/Providers/AzureProviderTests.cs`** (create)
   - Azure SDK credential injection tests
   - Model listing and availability
   - Fallback behavior on credential failure
   - Azure-specific response handling

6. **`.worktrees/litellm-gateway-tester/**`** (worktree-specific)
   - Any temporary test data or fixtures

### Files You Must NOT Edit

These files are owned by other agents. **Do not modify them:**

- `Blaze.LlmGateway.Api/**` (owned by Coder)
- `Blaze.LlmGateway.AppHost/**` (owned by Infra)
- `Blaze.LlmGateway.ServiceDefaults/**` (owned by Infra)
- Any other project files not listed above

---

## Requirements (from PRD)

### Unit Test Coverage

**ChatCompletionsEndpoint:**
```csharp
[Fact]
public async Task ChatCompletions_ValidRequest_ReturnsStreamingResponse()
{
  // Arrange: Mock ChatClient, prepare request
  var request = new ChatCompletionRequest
  {
    Model = "gpt-4",
    Messages = new[] { new ChatMessage { Role = "user", Content = "Hello" } },
    Stream = true
  };
  
  // Act: Call endpoint
  var response = await client.PostAsJsonAsync("/v1/chat/completions", request);
  
  // Assert: Validate SSE stream format
  Assert.NotEmpty(stream); // Contains chunks
  Assert.Contains("[DONE]", lastChunk); // Ends with [DONE]
}
```

Similar tests for:
- Invalid requests (missing fields, invalid types)
- Error responses (400, 401, 500)
- Non-streaming mode

**CompletionsEndpoint:**
- Text-only response format
- Streaming termination
- Error handling

**ModelsEndpoint:**
- Model list structure
- Provider detection
- Empty provider fallback

### Integration Test Coverage

**LiteLlmCompatibilityTests:**
- End-to-end HTTP calls
- Streaming SSE format validation
- Provider selection logic
- Batch requests (if applicable)

**AzureProviderTests:**
- Azure SDK initialization
- Model discovery from Azure
- Credential handling
- Model availability

### Coverage Targets

| Target | Threshold | Critical |
|--------|-----------|----------|
| New endpoint code | **95%** | ✅ Yes |
| Overall solution | **>80%** | ✅ Yes |
| Lines covered | All hot paths | ✅ Yes |
| Branches covered | Error paths | ✅ Yes |

### Testing Framework

- **Framework:** xUnit (existing convention)
- **Mocking:** Moq (existing convention)
- **Coverage:** `XPlat Code Coverage` (proven in Tests project)
- **Runner:** `dotnet test --no-build --collect:"XPlat Code Coverage"`

---

## Artifacts to Read

Before starting, read these to understand the spec:

1. **`Docs/squad/runs/20260420-214331-litellm-gateway/prd.md`** — Full PRD with acceptance criteria
2. **`Docs/squad/runs/20260420-214331-litellm-gateway/plan.md`** — Detailed plan
3. **`Blaze.LlmGateway.Tests/`** — Existing test structure (reference)
4. **`CLAUDE.md`** — Code style and testing conventions
5. **Coder's handoff** (`handoff/coder-1.md`) — Endpoint contracts and schemas

---

## Expected Endpoint Behavior (from Coder)

### POST /v1/chat/completions

**Request:**
```json
{
  "model": "gpt-4",
  "messages": [{"role": "user", "content": "Hello"}],
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

### POST /v1/completions

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

### GET /v1/models

**Response:**
```json
{
  "object": "list",
  "data": [
    {"id": "gpt-4", "object": "model", "provider": "AzureFoundry"},
    {"id": "gpt-3.5-turbo", "object": "model", "provider": "Ollama"}
  ]
}
```

---

## Acceptance Criteria

✅ **Unit Tests:**
- [ ] ChatCompletionsEndpointTests: 10+ tests covering validation, streaming, errors
- [ ] CompletionsEndpointTests: 10+ tests covering text-only format
- [ ] ModelsEndpointTests: 5+ tests covering model discovery

✅ **Integration Tests:**
- [ ] LiteLlmCompatibilityTests: End-to-end HTTP tests (5+ tests)
- [ ] AzureProviderTests: Credential + model tests (5+ tests)

✅ **Coverage:**
- [ ] New endpoint code: **95% line coverage**
- [ ] Overall solution: **>80% line coverage**
- [ ] Coverage report generated with `XPlat Code Coverage`

✅ **Test Execution:**
- [ ] All tests pass: `dotnet test --no-build`
- [ ] Zero failures
- [ ] Zero skipped tests

✅ **Code Quality:**
- [ ] Tests use xUnit + Moq conventions
- [ ] Tests use meaningful assertions
- [ ] Tests follow AAA (Arrange-Act-Assert) pattern
- [ ] Mock objects configured correctly

---

## Inherited Assumptions

| Assumption | Status |
|-----------|--------|
| xUnit available in Tests project | ✅ |
| Moq available for mocking | ✅ |
| XPlat Code Coverage tool available | ✅ |
| Coder agent completes endpoints on-time | ✅ (Awaited) |
| Azure SDK contract known from PRD | ✅ |

---

## Test Data & Mocking

### Mock ChatClient

```csharp
var mockChatClient = new Mock<IChatClient>();
mockChatClient
  .Setup(c => c.CompleteStreamingAsync(
    It.IsAny<IList<ChatMessage>>(),
    It.IsAny<ChatOptions>(),
    It.IsAny<CancellationToken>()))
  .Returns(GenerateStreamResponse());
```

### Mock Azure Provider

```csharp
var mockAzureClient = new Mock<AzureOpenAIClient>();
mockAzureClient
  .Setup(c => c.GetChatClient(It.IsAny<string>()))
  .Returns(mockChatClient.Object);
```

---

## Checklist

- [ ] Read PRD, plan, Coder handoff, and this handoff
- [ ] Create worktree: `git worktree add -b feature/litellm-gateway-tester .worktrees/litellm-gateway-tester main`
- [ ] Switch to worktree
- [ ] Create all 5 test files with comprehensive coverage
- [ ] Verify tests compile: `dotnet build Blaze.LlmGateway.Tests --no-incremental`
- [ ] Run tests locally: `dotnet test Blaze.LlmGateway.Tests --no-build --collect:"XPlat Code Coverage"`
- [ ] Verify coverage meets gates (95% new, >80% overall)
- [ ] Commit changes: `git add -A && git commit -m "test: Add comprehensive LiteLLM endpoint test suite"`
- [ ] Emit `[DONE]` signal when complete

---

## Success Signal

When all acceptance criteria are met, emit:

```
[DONE] Task: Tester — LiteLLM endpoint test suite
Status: ✅ All tests passing (0 failures), coverage 95% new / >80% overall
Artifacts: ChatCompletionsEndpointTests.cs, CompletionsEndpointTests.cs, ModelsEndpointTests.cs, 
           LiteLlmCompatibilityTests.cs, AzureProviderTests.cs, coverage.xml
Reason: Comprehensive coverage achieved, all acceptance criteria met
```

---

*Handoff created by squad-orchestrator at 2026-04-20T21:43:45Z*  
*Run ID: 20260420-214331-litellm-gateway*
