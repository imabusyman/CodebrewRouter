# Blaze.LlmGateway — Reality Check, 2026-04-24

## 1. What the repo actually contains today

### Providers wired (the real answer: 2 working, 1 broken, 1 internal)

| Key | Registered in code? | Actual working? | Notes |
|---|---|---|---|
| AzureFoundry | ✅ InfrastructureServiceExtensions.cs:24 | ✅ yes — needs endpoint+key user-secrets | Uses AzureOpenAIClient + GetChatClient(model).AsIChatClient(). Default model gpt-4o. |
| FoundryLocal | ✅ line 35 | 🟡 depends on localhost:5273 being up | Uses AzureOpenAIClient against http://localhost:5273 with literal key "notneeded". AppHost Foundry container is commented out (AppHost/Program.cs:40-41). |
| OllamaLocal | ✅ line 46 | 🟡 depends on http://localhost:11434 | Registered but intentionally not a selectable destination — used as the router-brain classifier and nothing else. Not in RouteDestination enum, not in /v1/models. |
| GithubModels | ❌ never registered | ❌ BROKEN | This is the bug. See §2.1. |
| CodebrewRouter | ✅ line 126 | 🟡 virtual | Task-classifying facade over the three real providers. |

And RouteDestination is a 3-value enum (Core/RouteDestination.cs): **AzureFoundry**, **FoundryLocal**, **GithubModels**.

### Models

- **Azure Foundry** — dynamically discovered via `GET {endpoint}/openai/v1/models` (AzureFoundryModelDiscovery.cs:24). Whatever deployments exist on your Azure OpenAI resource show up.
- **Foundry Local** — single model id read from config `LlmGateway:Providers:FoundryLocal:Model` (currently empty string in appsettings.json).
- **GitHub Models** — AppHost declares two resources (openai/gpt-4o-mini, microsoft/phi-4-mini-instruct) (AppHost/Program.cs:43-46) but nothing reads them as an IChatClient.
- **CodebrewRouter** — the virtual model "codebrewRouter" appears in /v1/models when CodebrewRouter.Enabled=true (ModelCatalogService.cs:70).
- **Ollama** — default llama3.2 used only as the classifier; not listed in /v1/models.

### Endpoints

Wired in LiteLlmEndpoints.cs (ProgramPartial.cs):

- `POST /v1/chat/completions` — SSE streaming + non-streaming (ChatCompletionsEndpoint.cs)
- `POST /v1/completions` — legacy text completions
- `GET /v1/models` — merged Azure-discovered + configured
- `GET /openapi/v1.json` + `/scalar` — OpenAPI doc + Scalar UI
- `GET /health`, `/alive` — via MapDefaultEndpoints()

Nothing else. No embeddings, no audio, no images, no rerank, no admin, no auth.

### Pipeline (MEAI)

```
app: IChatClient (unkeyed)
  = LlmRoutingChatClient (DelegatingChatClient)
      .InnerClient = GithubModels ?? AzureFoundry ?? FoundryLocal  ← first-null fallback
      .Strategy    = OllamaMetaRoutingStrategy(OllamaLocal) ?? KeywordRoutingStrategy
      .Failover    = ConfiguredFailoverStrategy
      resolves target via sp.GetKeyedService<IChatClient>(destination)
        ├── AzureFoundry → AzureOpenAIClient → AsIChatClient → UseFunctionInvocation
        ├── FoundryLocal → AzureOpenAIClient → AsIChatClient → UseFunctionInvocation
        └── OllamaLocal  → OllamaApiClient   → AsIChatClient → UseFunctionInvocation
```

McpToolDelegatingClient is implemented (McpToolDelegatingClient.cs) and inherits DelegatingChatClient correctly, but it is fully commented out in Program.cs:46-57 and in InfrastructureServiceExtensions.cs:98-106. **MCP is currently dead**.

---

## 2. Critical bugs (don't ship until these are fixed)

### 2.1 GithubModels is never registered — silent failover collapse

`CodebrewRouterOptions.FallbackRules` (CodebrewRouterOptions.cs:22-30) has GithubModels in every rule, and Coding puts it first. But `AddLlmProviders()` never creates an `OpenAIClient` pointed at the GitHub Models endpoint and never calls `AddKeyedSingleton<IChatClient>("GithubModels", …)`. The AppHost does `builder.AddGitHubModel(...)` and injects `LlmGateway__Providers__GithubModels__ApiKey` env var, but there's no code on the receiving end that reads it. Also, `GithubModelsOptions` doesn't even exist in `ProvidersOptions` (LlmGatewayOptions.cs:12-17).

**Consequence:** every codebrewRouter request logs ⚠️ `provider 'GithubModels' not registered — skipping` and collapses to AzureFoundry. All the clever task-based routing is effectively "always send to Azure." Your tests don't catch this because they manually `AddKeyedSingleton<IChatClient>("GithubModels", mockClient.Object)` in every test setup.

### 2.2 OpenAI-compatibility is subtly broken

In ChatCompletionsEndpoint.cs:126:
```csharp
var chunk = new { id, @object = "text_completion.chunk", created, model, choices = ... };
```

OpenAI's actual spec is `"chat.completion.chunk"` for chat streaming and `"chat.completion"` for non-streaming (line 186 also wrongly emits `"text_completion"`). Any strict OpenAI client that validates the object field will reject your responses.

Also: no role emitted on the first delta chunk, no separate final-chunk with `finish_reason: "stop"` and empty delta — real OpenAI streams that shape. Some clients tolerate it, some don't.

### 2.3 Function calling is silently dropped

`ChatCompletionRequest.Tools` is parsed from the request body (OpenAiModels.cs:45), but `ChatCompletionsEndpoint.HandleAsync` never reads `req.Tools` and never sets `options.Tools`. So any client that sends tools/functions gets them thrown away on the floor. MEAI's `FunctionInvokingChatClient` is wired per-provider, but with zero tools in `ChatOptions.Tools` it does nothing.

### 2.4 Streaming failover is dead code

`LlmRoutingChatClient.cs:56-82` — the streaming path calls `targetClient.GetStreamingResponseAsync` directly and yields. Any exception bubbles up and kills the stream. `TryFailoverStreamingAsync` at line 135 exists but is never called from anywhere. The non-streaming path does call `TryFailoverAsync`. The `CodebrewRouterChatClient` implements it correctly with a first-chunk probe (CodebrewRouterChatClient.cs:72-139), so the codebrewRouter virtual model is actually fine — but the default `/v1/chat/completions` path without that model is not.

### 2.5 Vision (your Yardly blocker) isn't even representable on the wire

OpenAiModels.cs:48-50:
```csharp
public record ChatMessageDto(string Role, string Content);
```

`Content` is a scalar string. OpenAI's vision format is `content: [{type: "text", text}, {type: "image_url", image_url: {url}}]`. The deserializer will throw on a vision-formatted request. Even if it didn't, the conversion at line 67 `new ChatMessage(role, msg.Content)` flattens it to a text-only message.

MEAI itself (`ChatMessage.Contents = IList<AIContent>` with TextContent, DataContent, UriContent) supports vision just fine — it's the gateway's wire DTO + the conversion layer that strips it. **This is the #1 thing to fix before Yardly has any chance.**

---

## 3. What's missing compared to LiteLLM

LiteLLM's defining features, with a blunt score on each:

| Feature | LiteLLM | Blaze |
|---|---|---|
| # of providers | 100+ | 2 working + 1 broken |
| Vision / image input passthrough | ✅ | ❌ wire format blocks it |
| Tool/function calling | ✅ | ❌ dropped in the DTO layer |
| Embeddings `POST /v1/embeddings` | ✅ | ❌ |
| Image gen `POST /v1/images/generations` | ✅ | ❌ |
| Audio `/v1/audio/transcriptions` | ✅ | ❌ |
| Virtual API keys (per-client keys) | ✅ | ❌ no auth at all |
| Per-key budgets / spend caps | ✅ | ❌ |
| Cost tracking + spend reports | ✅ | ❌ Usage is always null |
| Token counting | ✅ | ❌ |
| Retries + timeouts per model | ✅ | 🟡 failover chain exists, no timeouts, no retry with backoff |
| Streaming failover | ✅ | 🟡 works on codebrewRouter only, dead on the default client |
| Load balancing across N deployments | ✅ | ❌ |
| Redis cache (exact + semantic) | ✅ | ❌ |
| Guardrails / PII redaction | ✅ | ❌ |
| Prompt management / versioning | ✅ | ❌ |
| Callbacks to Langsmith / Helicone / S3 logs | ✅ | ❌ |
| Admin UI | ✅ (their "proxy admin UI") | ❌ only Open WebUI as dev playground |
| Multi-tenancy | ✅ (teams/orgs) | ❌ |
| SSO / OAuth on admin | ✅ | ❌ |
| Rate limiting per key / per model | ✅ | ❌ |
| MCP server bridge | 🟡 (emerging) | ❌ disabled |
| OpenAI spec compliance on wire | ✅ | 🟡 wrong object values |
| Session store | optional | ❌ |

You have the MEAI pipeline scaffolding (which is genuinely nice — `DelegatingChatClient` middleware is cleaner than LiteLLM's Python callback model), plus a task-aware router that LiteLLM doesn't have natively. But on the business/SaaS-critical axes — auth, cost, multi-tenancy, vision — you are at zero.

---

## 4. PRD / code divergence (don't trust either PRD)

`Docs/PRD/blaze-llmgateway-prd.md` is dated April 17 and based on the pre-removal 9-provider world. It still lists Gemini, OpenRouter, GitHub Copilot, OllamaBackup as "✅ Registered" (§2.1, §2.2, §7.2) — commit 9e39a77 removed them. It still calls LlmRoutingChatClient a IChatClient direct implementation (§2.4) — in fact it already inherits DelegatingChatClient (line 14). It says MCP tool injection is "✅ Partial" — MCP is commented out entirely.

`Docs/PRD/litellm-compatible-gateway.md` is narrower and claims 3 endpoints + Azure SDK + Swagger + 95% coverage as the deliverable. Endpoints and Scalar are there; the coverage number is not verified.

The 10 ADRs in `Docs/design/adr/` mostly describe aspiration, not code:

| ADR | Status |
|---|---|
| ADR-0001 (co-hosted agent runtime) | no agent runtime exists |
| ADR-0002 (config-driven ProviderDescriptor + ModelProfile catalog) | not implemented; still using enum + static ProviderOptions classes |
| ADR-0004 (SQLite + EF Core ISessionStore) | not implemented; no DbContext, no migrations, no ISessionStore anywhere |
| ADR-0006 (IAgentAdapter) | not implemented |
| ADR-0008 (default-deny cloud escalation) | no enforcement code; the cloud-egress.instructions.md guardrail exists as a prompt rule only |

**Recommendation:** archive both PRDs as "historical aspiration" and write a new one from the current code.

---

## 5. Code-quality flags (your CLAUDE.md rules vs. reality)

| Rule | Status |
|---|---|
| MEAI is the law, no raw HttpClient for LLMs | ✅ respected for LLM calls; HttpClient is used in AzureFoundryModelDiscovery.cs:35 for the /models discovery, which is fine — not an LLM call |
| GetStreamingResponseAsync, not CompleteStreamingAsync | ✅ respected |
| Keyed DI for all providers | ✅ mostly — the unkeyed default IChatClient is a LlmRoutingChatClient wrapping keyed resolutions |
| DelegatingChatClient for middleware | ✅ LlmRoutingChatClient, McpToolDelegatingClient, CodebrewRouterChatClient all inherit it. The PRD claim that they don't is stale |
| Program.cs clean, DI in extension methods | ✅ AddLlmProviders, AddLlmInfrastructure, RegisterLiteLlmEndpoints |
| Primary constructors + collection expressions | ✅ consistently used |
| Warnings as errors | claimed, unverified — I'd run the build to confirm |
| Tests at 95% | integration-test scaffolding is real and mostly working via WebApplicationFactory<Program>. Coverage number unverified and dubious given how much code silently skips in prod |

---

## 6. Research list — what to go learn

Ordered by leverage-per-hour:

1. **LiteLLM's virtual-key + spend model.** Read litellm/proxy/proxy_server.py and the "LiteLLM proxy config.yaml" docs. Copy the shape: a YAML/JSON model_list of `{model_name, litellm_params, model_info}`, and a general_settings block with master_key, database_url, redis_url, budget_duration. The data model for virtual keys + teams + budgets is what makes it a SaaS-friendly product and is independent of the provider SDK layer.

2. **Microsoft.Extensions.AI content parts + vision.** Read the current MEAI source for `ChatMessage.Contents`, `TextContent`, `DataContent`, `UriContent`. Then look at how Azure.AI.OpenAI and OpenAI NuGet clients accept multimodal input. Your Yardly blocker is here.

3. **MEAI's ChatClientBuilder extensions** — `UseFunctionInvocation`, `UseOpenTelemetry`, `UseLogging`, `UseDistributedCache`. `UseDistributedCache` + Redis is a 5-line solution for LiteLLM's exact-match cache; you do not need to write it yourself.

4. **OpenAI's streaming SSE exact spec** — the Chat Completions object shapes (chat.completion.chunk, role in first chunk, finish_reason in last, [DONE]). There's an OpenAI compatibility test suite embedded in ollama and vLLM that's useful.

5. **AspNetCore RateLimiter + token-bucket** for per-key RPM/TPM limits, and OpenTelemetry semantic conventions for GenAI (gen_ai.system, gen_ai.request.model, gen_ai.usage.input_tokens, etc. — use these for spend and observability in one shot).

6. **Managed Identity / Entra auth** on ASP.NET minimal APIs for the admin/control plane once you have tenants.

7. **LiteLLM's fallback config** — they support a `fallbacks: [{model: gpt-4, fallback: [gpt-3.5, claude-3]}]` structure that closely maps to your FailoverChains. Re-using their config shape buys you 1:1 compatibility for users migrating.

8. **MCP in MEAI 2026** — the Agent Framework samples have a pattern for `HostedMcpServerTool` that the `McpToolDelegatingClient` stub references but doesn't implement. If MCP is part of the value prop, un-stubbing this is a week of work.

---

## 7. Top 10 things to do before this is a viable LiteLLM alternative

Ordered to maximize early user-visible value:

1. **Fix the OpenAI wire format** — `chat.completion.chunk` / `chat.completion` object ids, role on first delta, finish_reason on last. Non-negotiable.

2. **Register GithubModels as a keyed IChatClient** (add `GithubModelsOptions` to `ProvidersOptions`, build an `OpenAIClient` with `https://models.inference.ai.azure.com` endpoint + PAT, wrap with `.AsBuilder().UseFunctionInvocation().Build()`, register keyed). Add a Tier-A integration test that actually resolves keyed services without substituting mocks, so this bug class cannot recur.

3. **Make ChatMessageDto.Content polymorphic** — accept `string | Array<ContentPart>`, where parts are `{type: "text"|"image_url"|"input_audio", ...}`. Translate into `ChatMessage.Contents` with TextContent, UriContent/DataContent. Add a Yardly-flavored integration test that sends an image URL.

4. **Forward ChatCompletionRequest.Tools into ChatOptions.Tools** — translate to MEAI AIFunction / HostedMcpServerTool. Otherwise function calling is a lie.

5. **API key / virtual key auth middleware** — simplest useful v1: `Authorization: Bearer sk-<key>`, keys stored in a SQLite table, per-key allowed-model list. This is the single feature that turns this from "your router" into "a product you can give others."

6. **Per-key spend tracking** — EF Core table, record tokens + estimated cost per request (pricing table in config), background flush. Display in a simple admin `/admin/keys` endpoint. Do this before any other admin features.

7. **Wire the streaming failover in LlmRoutingChatClient** (or delete it and rely on codebrewRouter only, but pick one — dead code will bite you).

8. **Request/response cache via UseDistributedCache + Redis** on the pipeline, header `X-LlmGateway-NoCache: true` to bypass. Two-hour task with MEAI.

9. **Rate limiting via Microsoft.AspNetCore.RateLimiting** — token-bucket per-key TPM/RPM, 429 with Retry-After.

10. **Admin UI** — don't build it yourself. Point Open WebUI at it for chat, add a thin Blazor `/admin` page for virtual-key CRUD + spend. Anything more ambitious is a distraction.

---

## 8. What you should not do

- **Don't re-add the 6 removed providers** (Gemini, OpenRouter, Copilot, OllamaBackup, etc.) until the 3 that are left actually work end-to-end, have tests that exercise real DI (not mocked-in-test-only), and have vision + function-calling passthrough.

- **Don't rewrite anything to match the April-17 PRD** — it's describing a world that doesn't exist.

- **Don't build DevUI / agent runtime** (ADR-0001, ADR-0006) yet. Those are interesting for v2. For a LiteLLM alternative powering SaaS, virtual keys and vision come first by a mile.

- **Don't implement MCP** until you have one real SaaS tool calling your gateway. MCP is a force multiplier only after you have a user.
