# Research Report: Recreating LiteLLM Supported Endpoints in Blaze.LlmGateway

## Executive Summary

LiteLLM's `supported_endpoints` page is not just a docs index; it reflects a large proxy surface that combines OpenAI-compatible APIs, provider-native APIs, resource-management APIs, and proxy-only utility APIs under one gateway.[^1] In source, LiteLLM implements that breadth by mounting many small FastAPI routers onto a single app, then funneling most request handling through a shared `ProxyBaseLLMRequestProcessing` pipeline and a `route_type` dispatch table instead of duplicating per-endpoint business logic.[^2][^3][^4]

The implication for Blaze.LlmGateway is significant: your current API exposes only `POST /v1/chat/completions`, manually deserializes messages, and directly streams SSE from `IChatClient`, so it is architecturally much closer to a single OpenAI-compatible façade than to LiteLLM's multi-resource gateway.[^5] If the goal is to "recreate these endpoints" rather than merely document them, the real work is not just adding more routes; it is introducing a shared request/response processing layer, route-type dispatch, auth/metadata handling, and persistence for resource-style endpoints like files, batches, responses background state, and vector stores.[^3][^4][^5]

The highest-leverage recreation strategy is phased. First, build a common endpoint runtime and expand the OpenAI-compatible core (`/chat/completions`, `/completions`, `/embeddings`, `/responses`, `/images`, `/audio`, `/moderations`). Then add persisted resource APIs (`/files`, `/batches`, `/vector_stores`, `/vector_stores/{id}/files`, `/rag/*`). Only after that should you add protocol-specific surfaces like Anthropic `/v1/messages`, Google `:generateContent`, Realtime/WebRTC, and MCP, because those depend on the same shared runtime but require materially different wire protocols and auth models.[^1][^3][^4][^6][^7][^8][^9]

## Architecture / System Overview

```text
                 LiteLLM docs index
        (/responses, /files, /batches, /mcp, ...)
                           |
                           v
          ┌──────────────────────────────────────┐
          │ FastAPI app in proxy_server.py       │
          │ - core routes in main file           │
          │ - many mounted sub-routers           │
          └──────────────────────────────────────┘
                           |
          ┌────────────────┼────────────────┐
          |                |                |
          v                v                v
   OpenAI-style      Provider-native     Proxy-specific
   route aliases     surfaces            resources/tools
   (/v1/*, Azure)    (/v1/messages,      (/mcp, /rag/*,
                     :generateContent,    /search, guardrails,
                     /realtime, etc.)     vector store mgmt)
                           |
                           v
        ┌──────────────────────────────────────┐
        │ ProxyBaseLLMRequestProcessing        │
        │ - add metadata/auth context          │
        │ - pre-call checks + hooks            │
        │ - streaming/non-streaming handling   │
        │ - custom headers + error mapping     │
        └──────────────────────────────────────┘
                           |
                           v
          ┌────────────────────────────────────┐
          │ route_request(route_type, data)    │
          │ ROUTE_ENDPOINT_MAPPING             │
          └────────────────────────────────────┘
                           |
                           v
                provider/router-specific call
```

LiteLLM's app composition is explicit in `proxy_server.py`: the main file owns chat/completions, completions, embeddings, moderations, audio, realtime websocket, and assistants, but then mounts separate routers for responses, batches, rerank, OCR, RAG, videos, containers, search, images, fine tuning, vector stores, vector-store files, files, MCP, Anthropic, Google, pass-through routes, evals, A2A, and more.[^2] That structure is the main architectural lesson for Blaze.LlmGateway: the endpoint count is large, but LiteLLM keeps it maintainable by splitting protocol/resource families into modules while centralizing request processing.[^2][^3]

## Endpoint Taxonomy from the LiteLLM Docs Surface

The docs index advertises four distinct classes of endpoint surface, not one homogeneous OpenAI clone.[^1]

| Family | Examples from docs index | What it implies |
|---|---|---|
| OpenAI-compatible generation APIs | `/chat/completions`, `/completions`, `/embeddings`, `/responses`, `/images/edits`, `Image Generations`, `/audio/transcriptions`, `/audio/speech`, `/moderations`, `/batches`, `/files`, `/fine_tuning`, `/vector_stores`, `/vector_stores/{id}/files`[^1] | You need request/response compatibility plus resource persistence for IDs and retrieval. |
| Provider-native protocol surfaces | `/v1/messages`, `/v1/messages/count_tokens`, `/generateContent`, `/interactions`, `/converse`, `/invoke`, `/realtime`[^1] | You need protocol-specific request shapes, not only OpenAI DTOs. |
| Proxy-native utility/resource APIs | `/mcp`, `/rag/ingest`, `/rag/query`, `/search`, `/guardrails/apply_guardrail`, `/skills`, `/ocr`, `/videos`, `/evals`, `/a2a`[^1] | LiteLLM is acting as an AI gateway platform, not only a model router. |
| Pass-through/compatibility surfaces | `Pass-through Endpoints (Anthropic SDK, etc.)`, Azure-compatible OpenAI paths, provider-prefixed resource paths[^1][^10][^11][^12] | Compatibility aliases are a deliberate feature and part of the public contract. |

## How LiteLLM Implements the Surface

### 1. Router composition over one giant controller

LiteLLM keeps the app broad but modular by including many routers into one FastAPI app. The mount list in `proxy_server.py` shows `response_router`, `batches_router`, `image_router`, `fine_tuning_router`, `vector_store_router`, `vector_store_management_router`, `vector_store_files_router`, `openai_files_router`, `anthropic_router`, `google_router`, `search_router`, `video_router`, `rag_router`, `mcp_*` routers, `a2a_router`, and many others.[^2] That means the documentation index maps fairly directly to source modules: most endpoint families have a dedicated `.../endpoints.py` file rather than being hidden behind one overloaded handler.[^2]

### 2. Shared processing layer instead of endpoint-specific logic

The central abstraction is `ProxyBaseLLMRequestProcessing`. Its `common_processing_pre_call_logic()` takes a `route_type` plus request, auth, proxy config, user overrides, and router state; the accepted `route_type` values cover chat, embeddings, responses, realtime, batches, files, image edits, Google content, vector stores, vector-store files, OCR, search, videos, containers, skills, Anthropic messages, interactions, and evals.[^3] Its `base_process_llm_request()` then runs common pre-call processing, launches moderation/during-call hooks in parallel, dispatches through `route_request()`, and normalizes streaming vs non-streaming responses, custom headers, and error handling.[^4]

The routing side is deliberately table-driven. `ROUTE_ENDPOINT_MAPPING` maps internal route types like `acompletion`, `aembedding`, `aresponses`, `aimage_edit`, `asearch`, `avideo_generation`, `acreate_realtime_client_secret`, `aingest`, `acreate_interaction`, and `acreate_eval` to public endpoint shapes like `/chat/completions`, `/embeddings`, `/responses`, `/images/edits`, `/search`, `/videos`, `/realtime/client_secrets`, `/rag/ingest`, `/interactions`, and `/evals`.[^6] That pattern is the cleanest model for Blaze.LlmGateway to copy: add routes freely, but collapse them onto a small internal enum/route key instead of duplicating processing logic in every endpoint.[^3][^4][^6]

### 3. OpenAI-compatible core endpoints are only the base layer

The core OpenAI routes live directly in `proxy_server.py`. Chat completions are exposed at `/v1/chat/completions`, `/chat/completions`, `/engines/{model:path}/chat/completions`, and `/openai/deployments/{model:path}/chat/completions`, with Azure-compatible path aliases baked in.[^10] Text completions and embeddings follow the same alias strategy, again including Azure deployment-style paths.[^10]

That aliasing matters because LiteLLM is not only normalizing providers; it is normalizing clients. If you want LiteLLM-like compatibility, Blaze.LlmGateway should not stop at `/v1/...` paths. It should decide explicitly whether to support unversioned aliases and Azure-compatible deployment aliases for the OpenAI-style families it adopts.[^10][^11]

### 4. `/responses` is more than another generation endpoint

The responses router is effectively a stateful superset surface. LiteLLM exposes `POST /v1/responses`, `GET /v1/responses/{response_id}`, `DELETE /v1/responses/{response_id}`, `GET /v1/responses/{response_id}/input_items`, `POST /v1/responses/compact`, `POST /v1/responses/{response_id}/cancel`, and websocket variants, with mirrored `/responses` and `/openai/v1/responses` aliases.[^7][^13]

The important implementation detail is background state. `POST /v1/responses` can switch into polling mode when `background=true`, generate a polling ID, store initial state in Redis, and stream completion work in the background, while the `GET`, `DELETE`, and `cancel` handlers understand both provider response IDs and LiteLLM polling IDs.[^7][^13] Recreating `/responses` in Blaze.LlmGateway therefore implies more than request-shape parity: you need a state store for background jobs and response lifecycle APIs, or else a deliberately reduced scope where you only support synchronous responses and omit polling/cancel/retrieve semantics.[^7][^13]

### 5. Resource APIs are first-class: files, batches, vector stores

LiteLLM treats files and batches as separate API families, not incidental helpers. The files router supports `POST /v1/files`, `GET /v1/files`, `GET /v1/files/{file_id}`, `GET /v1/files/{file_id}/content`, and `DELETE /v1/files/{file_id}`, with bare `/files` aliases and provider-prefixed variants like `/{provider}/v1/files`.[^8] The batches router likewise supports create, retrieve, list, and cancel at both `/v1/batches` and `/{provider}/v1/batches` forms.[^14]

Vector stores are split in two ways. First, LiteLLM has OpenAI-style vector store CRUD/search routes like `POST /v1/vector_stores`, `GET /v1/vector_stores`, `GET/POST/DELETE /v1/vector_stores/{vector_store_id}`, and `POST /v1/vector_stores/{vector_store_id}/search`.[^15] Second, it has proxy-native management routes such as `/vector_store/new`, `/vector_store/list`, `/vector_store/delete`, `/vector_store/info`, and `/vector_store/update`, backed by a database and an in-memory registry that is synchronized from the database as the source of truth.[^16]

That split is important for recreation planning. If you only implement the OpenAI vector store endpoints in Blaze.LlmGateway without adding a persistence abstraction, you can mimic the HTTP shape but not the actual behavior LiteLLM offers around long-lived managed vector stores, team access control, and reconciliation between durable state and in-memory routing state.[^15][^16]

### 6. Managed resource IDs are part of the contract

LiteLLM's vector-store-file endpoints demonstrate that resource IDs are not opaque pass-through values. The vector-store-file router decodes managed file IDs and managed vector-store IDs, uses them to recover routing/model information, substitutes provider-specific IDs on the way down, checks permissions, and then restores the original managed IDs in responses so the client sees a stable proxy-level ID.[^9] The same router exposes a full family of file association operations under `/v1/vector_stores/{vector_store_id}/files`, `.../{file_id}`, and `.../{file_id}/content` with shared processing route types for create/list/retrieve/content/update/delete.[^9]

If Blaze.LlmGateway wants LiteLLM-like resource APIs, it needs a similar proxy-owned resource identifier scheme. Without that, your gateway can still forward provider IDs, but you lose one of LiteLLM's biggest interoperability features: the ability to hide provider-specific resource naming and credentials behind gateway-managed IDs.[^9][^16]

### 7. Provider-native surfaces are intentionally different

LiteLLM does not try to coerce every client into OpenAI JSON. Anthropic support is exposed as `/v1/messages` and `/v1/messages/count_tokens`, with a distinct response path and Anthropic-specific error translation.[^17] Google support is exposed as `/v1beta/models/{model_name:path}:generateContent`, `:streamGenerateContent`, `:countTokens`, plus the Google Interactions API under `/v1beta/interactions` and related ID routes.[^18]

Realtime is split across two surfaces. The main websocket handler in `proxy_server.py` exposes `/v1/realtime` and `/realtime`.[^11] Separate WebRTC endpoints create ephemeral client secrets at `/v1/realtime/client_secrets` and then accept `/v1/realtime/calls`, where the bearer token is an encrypted ephemeral token rather than the normal proxy API key.[^19] That means Blaze.LlmGateway should treat Realtime as its own sub-architecture, not as "chat completions with a websocket."[^11][^19]

### 8. Proxy-native APIs extend the gateway beyond model invocation

Some LiteLLM endpoints are clearly gateway features rather than provider surfaces. `/guardrails/apply_guardrail` loads a configured guardrail and applies it directly to text input for testing or shared guardrail usage.[^20] `/rag/ingest` and `/rag/query` are all-in-one workflows that combine file/URL ingestion, vector-store persistence, search, optional rerank, and response generation behind single endpoints.[^21]

Search and video follow the same pattern. `/v1/search` and `/v1/search/{search_tool_name}` route through a search-tool registry while preserving a Perplexity-style request/response shape.[^22] `/v1/videos`, `/v1/videos/{video_id}`, and related video routes are implemented as a dedicated family routed through the shared processor, not as ad hoc provider passthroughs.[^23]

The key lesson is scope. If you want only "LiteLLM's supported model invocation endpoints," you can stop earlier. If you literally want parity with the supported-endpoints page, Blaze.LlmGateway becomes a broader AI gateway product with resource, search, guardrail, and orchestration APIs.[^1][^20][^21][^22][^23]

## What Blaze.LlmGateway Currently Has

Your current API is intentionally narrow. `Program.cs` configures providers and infrastructure, then maps a single `POST /v1/chat/completions` endpoint, deserializes `messages` manually from JSON, converts them to `ChatMessage`, and streams SSE chunks from `IChatClient.GetStreamingResponseAsync(...)` followed by `data: [DONE]`.[^5] There is no equivalent shared endpoint runtime, no auth dependency at the HTTP layer, no persisted resource APIs, and no parallel route families for responses, files, batches, vector stores, or provider-native protocols.[^5]

That means the architectural delta from Blaze.LlmGateway to LiteLLM is not "add some extra route methods." It is "introduce a gateway runtime." At minimum, that runtime needs: request normalization, auth context/metadata injection, route-type dispatch, standard error mapping, common streaming helpers, and persistence abstractions for any endpoint family that returns durable IDs or supports follow-up operations.[^3][^4][^5][^7][^8][^14][^15]

## Recommended Recreation Plan for Blaze.LlmGateway

### Phase 1: Build the shared runtime before adding many endpoints

The first priority should be an internal dispatch model similar to LiteLLM's `route_type` + `ROUTE_ENDPOINT_MAPPING`, but expressed in your .NET conventions. In practice, that means defining endpoint groups that all delegate into one shared request processor, rather than letting each minimal API endpoint talk directly to `IChatClient` with its own serialization and streaming code.[^3][^4][^6]

Concretely for Blaze.LlmGateway, this suggests:

1. Add a shared request model binder / parser layer for JSON, multipart, and query/path param enrichment, analogous to LiteLLM's request parsing utilities.[^24]
2. Add a common processing service that can inject auth/user metadata, apply policy/guardrails/MCP augmentation, choose a `route_type`, and standardize streaming/non-streaming output.[^3][^4]
3. Keep provider invocation behind your existing routing infrastructure (`AddLlmProviders()`, `AddLlmInfrastructure()`, `IChatClient`) and add specialized abstractions only when an endpoint family cannot map cleanly to `IChatClient` alone, such as files, vector stores, or realtime.[^5]

### Phase 2: Add endpoints in dependency order, not docs order

If the end goal is broad LiteLLM-style coverage, the most sensible implementation order is:

| Priority | Endpoint families | Why first / why later |
|---|---|---|
| 1 | `/chat/completions`, `/completions`, `/embeddings`, `/moderations` | These fit best with existing request/response routing patterns and establish the common runtime.[^10][^3] |
| 2 | `/responses` | High user value, but requires lifecycle state and possibly background polling/cancel/retrieve support.[^7][^13] |
| 3 | `/images/*`, `/audio/*` | Still generation APIs, but require multipart/body variations and media-specific DTOs.[^12][^25] |
| 4 | `/files`, `/batches` | Introduce durable resources and provider/resource routing concerns.[^8][^14] |
| 5 | `/vector_stores`, `/vector_stores/{id}/files`, `/rag/*` | Require persistence, access control, and proxy-owned resource IDs for a strong implementation.[^9][^15][^16][^21] |
| 6 | `/v1/messages`, `:generateContent`, `/realtime`, `/mcp`, `/search`, `/videos`, `/guardrails/apply_guardrail` | These are protocol- or platform-specific surfaces that benefit from the runtime and persistence already being in place.[^11][^17][^18][^19][^20][^22][^23] |

### Phase 3: Decide what **not** to recreate

You probably do **not** want literal parity with everything on the docs page on day one. LiteLLM's page includes A2A, Evals, MCP, pass-through endpoints, skills, search tooling, vector stores, videos, OCR, and RAG pipelines because LiteLLM is a general-purpose AI gateway platform.[^1][^2] Blaze.LlmGateway today is a model-routing proxy with MCP augmentation around `IChatClient`, so the pragmatic decision is to pick a subset that aligns with your product direction rather than copying every surface uncritically.[^5]

For this project, the most natural "LiteLLM-like but not LiteLLM-sized" subset is: `/chat/completions`, `/completions`, `/embeddings`, `/responses`, `/images/*`, `/audio/*`, `/files`, `/batches`, and optionally `/vector_stores/*` if you are willing to add storage.[^5][^7][^8][^10][^12][^14][^15] The provider-native Google/Anthropic/Realtime/MCP/search/video families are better treated as later expansions once the gateway runtime exists.[^11][^17][^18][^19][^22][^23]

## Key Code Surfaces Summary

| Surface | Purpose | Key file(s) |
|---|---|---|
| LiteLLM docs index | Public endpoint inventory | `https://docs.litellm.ai/docs/supported_endpoints`[^1] |
| Main proxy app | Mounts routers, owns core OpenAI-style + realtime websocket routes | `proxy_server.py`[^2][^10][^11] |
| Shared endpoint runtime | Common pre-call processing, dispatch, streaming, headers | `common_request_processing.py`, `route_llm_request.py`[^3][^4][^6] |
| Responses API | Stateful generation, retrieve/delete/cancel/compact, websocket | `response_api_endpoints/endpoints.py`[^7][^13] |
| Files / batches | Durable resource APIs with provider aliases | `openai_files_endpoints/files_endpoints.py`, `batches_endpoints/endpoints.py`[^8][^14] |
| Vector stores | OpenAI-style CRUD/search + proxy-native management + vector-store files | `vector_store_endpoints/endpoints.py`, `vector_store_endpoints/management_endpoints.py`, `vector_store_files_endpoints/endpoints.py`[^9][^15][^16] |
| Provider-native surfaces | Anthropic, Google, Realtime | `anthropic_endpoints/endpoints.py`, `google_endpoints/endpoints.py`, `realtime_endpoints/endpoints.py`, `proxy_server.py`[^11][^17][^18][^19] |
| Proxy-native extras | Guardrails, RAG, search, videos | `guardrail_endpoints.py`, `rag_endpoints/endpoints.py`, `search_endpoints/endpoints.py`, `video_endpoints/endpoints.py`[^20][^21][^22][^23] |
| Blaze.LlmGateway current API | Current baseline: only `/v1/chat/completions` | `Blaze.LlmGateway.Api\\Program.cs`[^5] |

## Confidence Assessment

**High confidence**

- LiteLLM's public endpoint surface is materially broader than OpenAI chat compatibility alone, and the docs index explicitly lists the major families discussed above.[^1]
- The implementation pattern is clear: LiteLLM mounts many router modules, centralizes request processing in `ProxyBaseLLMRequestProcessing`, and uses `route_type` dispatch to avoid duplicating business logic per endpoint.[^2][^3][^4][^6]
- Blaze.LlmGateway currently exposes only `/v1/chat/completions` and lacks the shared runtime and persistence layers that would be required for true LiteLLM-style endpoint recreation.[^5]

**Moderate confidence**

- The recommended phase ordering is an engineering judgment based on the source structure and coupling between endpoint families, not a stated LiteLLM roadmap.[^3][^4][^5]
- Some LiteLLM families listed on the docs page, such as assistants, fine tuning, containers, and pass-through endpoints, were verified through app mounting and selected source inspection but not read as deeply as responses/files/vector-stores/realtime.[^1][^2]

**Main uncertainty**

- "Recreate these endpoints in my project" can mean either strict wire-compatibility for a selected subset or full functional parity with LiteLLM. The report assumes you want implementation guidance for a practical subset and therefore emphasizes architecture and dependency order over literal one-for-one parity with every route on the docs page.[^1][^5]

## Footnotes

[^1]: `https://docs.litellm.ai/docs/supported_endpoints` (fetched 2026-04-17)
[^2]: `sources/berriai-litellm/repo/proxy_server.py:252-289,13929-13999,14078-14236` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^3]: `sources/berriai-litellm/repo/common_request_processing.py:622-760` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^4]: `sources/berriai-litellm/repo/common_request_processing.py:879-1235` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^5]: `..\Blaze.LlmGateway.Api\Program.cs:10-65`
[^6]: `sources/berriai-litellm/repo/route_llm_request.py:16-97` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^7]: `sources/berriai-litellm/repo/response_api_endpoints.py:26-220` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^8]: `sources/berriai-litellm/repo/openai_files_endpoints.py:270-360,570-600,862-892,1051-1081,1253-1284` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^9]: `sources/berriai-litellm/repo/vector_store_files_endpoints.py:30-156,192-329,332-760` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^10]: `sources/berriai-litellm/repo/proxy_server.py:7106-7628` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^11]: `sources/berriai-litellm/repo/proxy_server.py:8109-8140` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^12]: `sources/berriai-litellm/repo/image_endpoints.py:49-176,205-290` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^13]: `sources/berriai-litellm/repo/response_api_endpoints.py:446-858,938-990` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^14]: `sources/berriai-litellm/repo/batches_endpoints.py:44-123,328-359,583-628,768-798` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^15]: `sources/berriai-litellm/repo/vector_store_endpoints.py:103-179,182-258,291-561` and `sources/berriai-litellm/repo/common_request_processing.py:658-669` and `sources/berriai-litellm/repo/route_llm_request.py:64-73` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^16]: `sources/berriai-litellm/repo/vector_store_management_endpoints.py:419-790` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^17]: `sources/berriai-litellm/repo/anthropic_endpoints.py:1-220` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^18]: `sources/berriai-litellm/repo/google_endpoints.py:19-260,313-455` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^19]: `sources/berriai-litellm/repo/realtime_endpoints.py:1-260` and `sources/berriai-litellm/repo/proxy_server.py:8128-8129` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^20]: `sources/berriai-litellm/repo/guardrail_endpoints.py:2130-2167` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^21]: `sources/berriai-litellm/repo/rag_endpoints.py:310-559` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^22]: `sources/berriai-litellm/repo/search_endpoints.py:15-239` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^23]: `sources/berriai-litellm/repo/video_endpoints.py:32-220,303-879` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^24]: `sources/berriai-litellm/repo/http_parsing_utils.py:16-170,240-256` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
[^25]: `sources/berriai-litellm/repo/proxy_server.py:7655-7913` and `sources/berriai-litellm/repo/image_endpoints.py:205-290` (LiteLLM ref `6a9f8f77726fe7e7ce2029c8c33909436dbf2117`)
