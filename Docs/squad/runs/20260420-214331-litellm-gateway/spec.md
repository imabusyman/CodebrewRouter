# Specification — LiteLLM-Compatible Gateway Implementation

**Orchestrator:** squad-orchestrator  
**Run ID:** 20260420-214331-litellm-gateway  
**Status:** Ready for Implementation  

---

## Overview

This specification details the parallel implementation of 3 LiteLLM-compatible endpoints (`/v1/chat/completions`, `/v1/completions`, `/v1/models`) with Azure OpenAI SDK integration, comprehensive testing, and Aspire orchestration.

---

## Endpoint Specifications

### 1. POST /v1/chat/completions

**Purpose:** OpenAI-compatible chat endpoint with streaming support

**Request Schema:**
```typescript
{
  "model": string,                  // e.g., "gpt-4", "gpt-3.5-turbo"
  "messages": [
    {
      "role": "system" | "user" | "assistant",
      "content": string
    }
  ],
  "temperature": number,            // 0.0–2.0, default 1.0
  "max_tokens": number,             // max completion tokens
  "stream": boolean,                // true for SSE, false for one-shot
  "top_p": number,                  // nucleus sampling, 0.0–1.0
  "frequency_penalty": number,      // -2.0–2.0
  "presence_penalty": number,       // -2.0–2.0
  "tools": [                        // optional
    {
      "type": "function",
      "function": { ... }
    }
  ]
}
```

**Response (Streaming, SSE):**
```
data: {"id":"chatcmpl-xxx","object":"text_completion.chunk","created":1234567890,"model":"gpt-4","choices":[{"index":0,"delta":{"role":"assistant","content":"Hello"},"finish_reason":null}]}

data: {"id":"chatcmpl-xxx","object":"text_completion.chunk","created":1234567890,"model":"gpt-4","choices":[{"index":0,"delta":{"content":" there"},"finish_reason":null}]}

data: [DONE]
```

**Response (Non-Streaming):**
```json
{
  "id": "chatcmpl-xxx",
  "object": "text_completion",
  "created": 1234567890,
  "model": "gpt-4",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Hello there!"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 10,
    "completion_tokens": 4,
    "total_tokens": 14
  }
}
```

**Error Responses:**
- `400 Bad Request` — Invalid request body (missing fields, invalid types)
- `401 Unauthorized` — Missing or invalid API key
- `500 Internal Server Error` — Server error (e.g., Azure SDK failure)

---

### 2. POST /v1/completions

**Purpose:** Legacy text-only completions endpoint (pre-chat API)

**Request Schema:**
```typescript
{
  "model": string,
  "prompt": string | string[],      // text to complete
  "max_tokens": number,
  "temperature": number,
  "top_p": number,
  "frequency_penalty": number,
  "presence_penalty": number,
  "stream": boolean
}
```

**Response (Streaming, SSE):**
```
data: {"id":"cmpl-xxx","object":"text_completion.chunk","created":1234567890,"model":"gpt-3.5-turbo","choices":[{"text":" in","index":0,"finish_reason":null}]}

data: {"id":"cmpl-xxx","object":"text_completion.chunk","created":1234567890,"model":"gpt-3.5-turbo","choices":[{"text":" a","index":0,"finish_reason":null}]}

data: [DONE]
```

**Response (Non-Streaming):**
```json
{
  "id": "cmpl-xxx",
  "object": "text_completion",
  "created": 1234567890,
  "model": "gpt-3.5-turbo",
  "choices": [
    {
      "text": " in a galaxy far far away",
      "index": 0,
      "finish_reason": "length"
    }
  ],
  "usage": {
    "prompt_tokens": 5,
    "completion_tokens": 6,
    "total_tokens": 11
  }
}
```

---

### 3. GET /v1/models

**Purpose:** Discover available models and providers

**Request:** None (query parameters optional)

**Response:**
```json
{
  "object": "list",
  "data": [
    {
      "id": "gpt-4",
      "object": "model",
      "provider": "AzureFoundry",
      "owned_by": "openai"
    },
    {
      "id": "gpt-3.5-turbo",
      "object": "model",
      "provider": "Ollama",
      "owned_by": "openai"
    },
    {
      "id": "phi-4-mini",
      "object": "model",
      "provider": "GithubModels",
      "owned_by": "microsoft"
    }
  ]
}
```

---

## Architecture

### Middleware Stack

```
HttpRequest
  ↓
  ├─ [CORS Middleware]
  ├─ [Authentication Middleware]
  └─ [Routing]
       ↓
       ├─ POST /v1/chat/completions → ChatCompletionsEndpoint
       │    ↓
       │    LlmRoutingChatClient (routes to appropriate provider)
       │    ├─ Azure OpenAI SDK ("AzureFoundry")
       │    ├─ Ollama Client
       │    ├─ Gemini Client
       │    └─ GitHub Models Client
       │
       ├─ POST /v1/completions → CompletionsEndpoint
       │    ↓
       │    [Convert to ChatMessage format] → LlmRoutingChatClient
       │
       └─ GET /v1/models → ModelsEndpoint
            ↓
            [Query all providers] → Return unified list

  ↓
  HttpResponse (with SSE streaming or JSON)
```

### Keyed DI Service Registration

```csharp
// In Program.cs (Coder task)
builder.Services.AddKeyedSingleton<IChatClient>("AzureFoundry", 
  (provider, key) => new AzureOpenAIClient(...).AsBuilder().UseFunctionInvocation().Build());

// Route to appropriate provider based on model name
services.AddScoped<IRoutingStrategy>(provider =>
  new DefaultRoutingStrategy(
    provider.GetRequiredKeyedService<IChatClient>("AzureFoundry"),
    provider.GetRequiredKeyedService<IChatClient>("Ollama"),
    provider.GetRequiredKeyedService<IChatClient>("GithubModels")
  )
);
```

---

## File Structure

### New Files (Created by Coder)

```
Blaze.LlmGateway.Api/
├── Endpoints/
│   ├── ChatCompletionsEndpoint.cs
│   ├── CompletionsEndpoint.cs
│   └── ModelsEndpoint.cs
├── Models/
│   └── OpenAiModels.cs
└── [Update Program.cs]
```

### New Test Files (Created by Tester)

```
Blaze.LlmGateway.Tests/
├── Endpoints/
│   ├── ChatCompletionsEndpointTests.cs
│   ├── CompletionsEndpointTests.cs
│   └── ModelsEndpointTests.cs
├── Integration/
│   └── LiteLlmCompatibilityTests.cs
└── Providers/
    └── AzureProviderTests.cs
```

### Modified Files (Infra)

```
Blaze.LlmGateway.AppHost/
└── [Update Program.cs]

Blaze.LlmGateway.ServiceDefaults/
└── [Update Extensions.cs]
```

---

## DTOs (OpenAiModels.cs)

### Request DTOs

```csharp
/// <summary>Chat completion request (OpenAI-compatible)</summary>
public record ChatCompletionRequest(
  string Model,
  IList<ChatMessage> Messages,
  double? Temperature = null,
  int? MaxTokens = null,
  bool Stream = false,
  double? TopP = null,
  double? FrequencyPenalty = null,
  double? PresencePenalty = null,
  IList<Tool>? Tools = null);

/// <summary>Text completion request (legacy)</summary>
public record TextCompletionRequest(
  string Model,
  string Prompt,
  int? MaxTokens = null,
  double? Temperature = null,
  double? TopP = null,
  double? FrequencyPenalty = null,
  double? PresencePenalty = null,
  bool Stream = false);

/// <summary>Tool specification</summary>
public record Tool(
  string Type,
  FunctionDefinition Function);

public record FunctionDefinition(
  string Name,
  string? Description = null,
  object? Parameters = null);
```

### Response DTOs

```csharp
/// <summary>Chat completion response</summary>
public record ChatCompletionResponse(
  string Id,
  string Object,
  long Created,
  string Model,
  IList<Choice> Choices,
  Usage? Usage = null);

public record Choice(
  int Index,
  ChatMessage? Message,
  ChoiceDelta? Delta,
  string? FinishReason);

public record ChoiceDelta(
  string? Role = null,
  string? Content = null);

public record TextCompletionResponse(
  string Id,
  string Object,
  long Created,
  string Model,
  IList<TextChoice> Choices,
  Usage? Usage = null);

public record TextChoice(
  int Index,
  string Text,
  string? FinishReason);

public record Usage(
  int PromptTokens,
  int CompletionTokens,
  int TotalTokens);

/// <summary>Models list response</summary>
public record ModelsResponse(
  string Object,
  IList<ModelInfo> Data);

public record ModelInfo(
  string Id,
  string Object,
  string Provider,
  string? OwnedBy = null);
```

---

## Error Handling

### Validation Errors (400)

```json
{
  "error": {
    "message": "Invalid request: missing required field 'model'",
    "type": "invalid_request_error",
    "param": "model",
    "code": "missing_field"
  }
}
```

### Authentication Errors (401)

```json
{
  "error": {
    "message": "Invalid API key",
    "type": "invalid_api_key_error",
    "code": "invalid_api_key"
  }
}
```

### Server Errors (500)

```json
{
  "error": {
    "message": "Internal server error",
    "type": "server_error",
    "code": "internal_error"
  }
}
```

---

## Testing Strategy

### Unit Test Scope

**ChatCompletionsEndpointTests:**
- Valid request with all fields
- Valid request with minimal fields
- Missing required field (`model`)
- Invalid message format
- Streaming vs. non-streaming
- Error scenarios (400, 401, 500)

**CompletionsEndpointTests:**
- Valid text prompt
- Missing required field (`prompt`)
- Invalid prompt format
- Streaming format validation
- Error scenarios

**ModelsEndpointTests:**
- Model list structure
- Provider detection (Azure, Ollama, Gemini, GitHub)
- Empty provider fallback
- Model count > 0

### Integration Test Scope

**LiteLlmCompatibilityTests:**
- End-to-end HTTP POST to `/v1/chat/completions`
- SSE chunk parsing
- `[DONE]` terminator validation
- Provider routing (prompt hints → correct provider)

**AzureProviderTests:**
- Azure SDK initialization
- Model discovery from Azure deployment list
- Credential handling (success + failure)
- Fallback to mock if credentials unavailable

### Coverage Goals

| Component | Target | Notes |
|-----------|--------|-------|
| ChatCompletionsEndpoint | 95%+ | All paths, error cases |
| CompletionsEndpoint | 95%+ | All paths, error cases |
| ModelsEndpoint | 95%+ | All paths |
| OpenAiModels (DTOs) | 90%+ | Serialization, validation |
| Integration tests | 80%+ | End-to-end flows |
| **Overall** | **>80%** | Solution-wide target |

---

## Swagger/OpenAPI Documentation

### Swagger UI Endpoints

- `GET /swagger/ui` — Interactive UI
- `GET /openapi.json` — OpenAPI 3.0 schema

### Documented Endpoints (in Swagger)

```yaml
/v1/chat/completions:
  post:
    summary: Create a chat completion
    description: Generate text from a chat prompt using OpenAI-compatible API
    parameters: []
    requestBody:
      content:
        application/json:
          schema: ChatCompletionRequest
    responses:
      200:
        description: Chat completion response (SSE or JSON)
      400:
        description: Invalid request
      401:
        description: Unauthorized
      500:
        description: Server error

/v1/completions:
  post:
    summary: Create a text completion
    description: Legacy text-only completion endpoint
    requestBody:
      content:
        application/json:
          schema: TextCompletionRequest
    responses:
      200:
        description: Text completion response (SSE or JSON)

/v1/models:
  get:
    summary: List available models
    description: Discover all available models and providers
    responses:
      200:
        description: List of available models
```

---

## Quality Gates

### Build Gate
```powershell
dotnet build --no-incremental -warnaserror
```
- ✅ Zero warnings
- ✅ Successful compilation
- ✅ No obsolete API usage

### Test Gate
```powershell
dotnet test --no-build --collect:"XPlat Code Coverage"
```
- ✅ All tests pass (0 failures)
- ✅ Coverage: 95% new endpoint code
- ✅ Coverage: >80% overall solution
- ✅ Coverage report generated

### Security Gate (ADR-0008)
- ✅ No new cloud-egress violations
- ✅ No unapproved cloud provider additions
- ✅ Credentials handled securely (no secrets in code)

---

## Success Criteria (Summary)

| Criterion | Target | Owner |
|-----------|--------|-------|
| 3 endpoints functional | ✅ | Coder |
| Streaming SSE format correct | ✅ | Coder |
| Azure SDK integrated | ✅ | Coder + Infra |
| Swagger documentation | ✅ | Coder |
| Unit tests (95% new) | ✅ | Tester |
| Integration tests | ✅ | Tester |
| Coverage >80% overall | ✅ | Tester |
| Build passes (-warnaserror) | ✅ | All |
| All tests passing | ✅ | Tester |
| Aspire orchestration | ✅ | Infra |

---

*Specification generated by squad-orchestrator*  
*Run ID: 20260420-214331-litellm-gateway*
