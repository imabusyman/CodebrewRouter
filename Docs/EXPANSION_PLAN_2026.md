# CodebrewRouter Expansion Plan: Offline SDK + RAG + Health Checks

**Created:** 2026-04-24  
**Target:** Extend Blaze.LlmGateway to support offline-first operation via an **SDK (OfflineLlmGateway.csproj)** that external apps like Yardly can integrate, plus integrated RAG with local + cloud sync, and real-time non-blocking health checks.

---

## 1. High-Level Vision

### Current State
- Blaze.LlmGateway: server-only LLM routing proxy (Azure Foundry, Foundry Local, Ollama)
- 4 critical bugs: GithubModels not registered, vision/content format broken, function calling dropped, wire format incorrect
- No RAG, no auth, no health checks, no offline support

### Future State
- **Core API** stays server-based (existing functionality)
- **Offline SDK** (shared .NET library, `OfflineLlmGateway.csproj`) for edge devices
- **Router brain** runs both server-side AND device-side via model sync
- **RAG pipeline** integrated into chat (retrieve → inject → route → respond) with local SQLite + cloud sync
- **Health checks** run async, prevent routing to dead models, cache results with lazy failure recovery

### Scope Constraints
- External apps (like Yardly) add OfflineLlmGateway as a NuGet dependency
- Offline mode tries API first (hybrid), falls back to local if unreachable
- RAG documents: local SQLite for offline, cloud DB for sync + backup
- Yardly uploads yard/plant identification data to RAG for offline use
- Health checks: async background tasks (no request blocking), gRPC health endpoint, lazy recovery on failure

---

## 2. Phase Decomposition

### Phase 1: Fix Critical Bugs (Prerequisite, ~2 days)
Before expanding, fix the four blockers from `Docs/summary/summary.md` §2:
- **Bug 1.1:** Register GithubModels as keyed IChatClient (add `GithubModelsOptions`, wire `OpenAIClient` to https://models.inference.ai.azure.com)
- **Bug 1.2:** Fix OpenAI wire format (`chat.completion.chunk` not `text_completion.chunk`, role on first delta, finish_reason on last)
- **Bug 1.3:** Make `ChatMessageDto.Content` polymorphic (string | Array<ContentPart>) to support vision
- **Bug 1.4:** Forward `ChatCompletionRequest.Tools` → `ChatOptions.Tools` to enable function calling

**Deliverable:** `dotnet build -warnaserror` + 95% test coverage, vision passthrough works end-to-end

---

### Phase 2: Health Check Infrastructure (~3 days)
**Goal:** Real-time provider availability detection without blocking requests

**Components:**
- **IProviderHealthStrategy** interface: async probe logic (TCP, HTTP HEAD, model inference test)
- **ProviderHealthMonitor** singleton: background task (configurable interval), caches status, exposes `GetProviderStatus()` query
- **HealthCheckEndpoint** (`GET /health/models`): JSON response of each provider's last-known status
- **gRPC Health endpoint** (optional): `grpc.health.v1.Health.Check` for K8s/load balancers
- **Routing integration**: `LlmRoutingChatClient` skips providers with `status == Down` before routing
- **Lazy recovery**: On any provider failure during a request, trigger async health check probe

**Configuration:**
```json
{
  "LlmGateway": {
    "HealthChecks": {
      "Enabled": true,
      "IntervalSeconds": 30,
      "TimeoutSeconds": 5,
      "ProbeStrategy": "lightweight_inference"  // or "tcp_connect", "http_head"
    }
  }
}
```

**Tests:**
- Mock a provider as Down, verify it's skipped during routing
- Trigger a failure, verify lazy recovery probe fires
- Verify health endpoint response shape matches OpenAI's format

**Deliverable:** Health status queryable, models auto-disabled when unreachable, no request-level blocking

---

### Phase 3: RAG Infrastructure (Shared Library) (~5 days)
**Goal:** Integrated retrieval-augmented generation, works both offline and online

**Components:**
- **RAGCore.csproj** (new shared library)
  - `IDocumentStore`: abstraction for SQLite / Cloud
  - `LocalSqliteDocumentStore`: embeddings + SQLite FTS5
  - `CloudDocumentStore`: (stub for now, cloud DB to be determined)
  - `RagEmbeddingService`: call OpenAI embeddings or use local embedder (e.g., AllMiniLm)
  - `RagRetrievalPipeline`: fetch top-K docs, inject into system prompt or messages
  
- **Integration into ChatCompletionsEndpoint:**
  - Parse `X-RAG-Query` header (optional) or use last user message as query
  - If RAG enabled: call `RagRetrievalPipeline.RetrieveAsync(query)` → inject docs into context
  - Pass enriched messages to router → provider
  
- **Yardly Document Format** (example):
  ```json
  {
    "doc_id": "yard-oak-identif-2026",
    "type": "plant_identification",
    "content": "Oak tree identification...",
    "tags": ["oak", "plant", "outdoor"],
    "source": "yardly_user_upload",
    "created_at": "2026-04-24"
  }
  ```

- **LocalSqliteDocumentStore Schema:**
  ```sql
  CREATE TABLE rag_documents (
    id TEXT PRIMARY KEY,
    content TEXT,
    embedding BLOB,  -- float32 array
    tags TEXT,       -- JSON array
    source TEXT,
    created_at TIMESTAMP,
    updated_at TIMESTAMP
  );
  CREATE VIRTUAL TABLE rag_documents_fts USING fts5(content, tags);
  CREATE INDEX idx_source ON rag_documents(source);
  ```

**Tests:**
- Upload a Yardly doc, retrieve it by keyword
- Verify embedding is stored and searched
- Verify retrieval injects into system prompt correctly

**Deliverable:** RAG library works offline with SQLite, accepts documents from Yardly, injects context into chat pipeline

---

### Phase 4: Offline SDK (Shared Library for External Apps) (~6 days)
**Goal:** External apps (like Yardly) can run CodebrewRouter locally on device

**Components:**
- **`OfflineLlmGateway.csproj` (new shared library)**
  - `OfflineRouterOptions`: local model list, RAG paths, router model config
  - `LocalModelRegistry`: manage Gemma-4 + router model bundled in app
  - `ILocalModelProvider`: abstraction for Ollama / ONNX Runtime / custom loaders
  - `OfflineRoutingStrategy`: uses router model locally to classify requests, select local model
  - `OfflineHealthMonitor`: probes local models (no network deps)
  - `LocalChatClient`: wraps local model inference, compatible with `IChatClient` interface
  - **External apps (like Yardly) add this NuGet package as a dependency**
  
- **Model Bundling Strategy:**
  - **Build-time bundling:** Include small quantized models (e.g., Gemma-4-int4, llama2-7b) in app bundle (5-10 MB each)
  - **Download-on-first-run:** Check local cache, download from CDN if missing (encrypted, signed)
  - **Hybrid:** Ship router model bundled, ship Gemma-4 as optional download
  
- **External App Integration (e.g., Yardly):**
  - Add `OfflineLlmGateway` NuGet package to their project
  - Instantiate `OfflineGatewayService` in DI container
  - Call `OfflineGatewayService.GetChatClientAsync()` to get local `IChatClient` instance
  - Uses same `ChatMessage` / `ChatOptions` MEAI contract as Blaze API
  - Fallback to Blaze API endpoint on network availability
  - Upload/download RAG docs via sync API

**Configuration (on device, in external app):**
```json
{
  "OfflineGateway": {
    "Enabled": true,
    "LocalModels": [
      {
        "id": "gemma-4-local",
        "path": "~/.codebrewrouter/models/gemma-4-int4.gguf",
        "downloadUrl": "https://cdn.yardly.app/models/gemma-4-int4.gguf",
        "checksumSha256": "abc123...",
        "sizeBytes": 5242880
      }
    ],
    "RouterModel": {
      "id": "router-local",
      "path": "~/.codebrewrouter/models/router.gguf",
      "bundled": true
    },
    "RagStorage": "~/.codebrewrouter/rag.db",
    "FallbackApiEndpoint": "https://blaze-api.yourdomain.com/v1"
  }
}
```

**Tests:**
- Offline SDK can classify and route a request locally
- Gemma-4 inference works via ONNX / local provider
- Falls back to API when offline SDK fails or user is online
- RAG retrieval works in offline mode

**Deliverable:** External apps can instantiate OfflineGateway, run local models, route requests, sync with cloud API

---

### Phase 5: Cloud-Offline Sync (Hybrid Operation) (~4 days)
**Goal:** RAG docs and routing state sync between device and server

**Components:**
- **SyncStateStore**: track device state (last_sync, pending_uploads, device_id)
- **CloudDocumentStore** (implement): accept document uploads from offline mode
- **RagSyncService**: background task to upload Yardly docs captured offline, download shared docs
- **RouterModelSyncService**: keep device router model in sync with server router (on-demand, not per-request)
- **ConflictResolution**: if doc edited both online and offline, last-write-wins with timestamp
- **Encryption**: docs in transit (TLS + optional field-level encryption for Yardly data)

**API endpoints (on server):**
- `POST /v1/rag/sync` — device sends changes, receives changes (batch sync)
- `POST /v1/rag/documents` — upload a document (Yardly origin)
- `GET /v1/router-model` — download latest router model (with version check)

**Configuration:**
```json
{
  "RagSync": {
    "Enabled": true,
    "SyncIntervalSeconds": 300,
    "ConflictStrategy": "last_write_wins",
    "EncryptionKey": "..."
  }
}
```

**Tests:**
- Upload doc on device, verify it appears in cloud RAG after sync
- Download doc from cloud, verify it's available in offline mode
- Conflict: same doc edited both sides → last-write wins

**Deliverable:** RAG documents sync between device and cloud, router model updated on-demand

---

### Phase 6: Integration + Polish (~3 days)
**Goal:** Wire all pieces, test end-to-end, update docs

**Components:**
- **Integration tests:**
  - External app sends vision request → OfflineGateway → local router classifies → Gemma-4 processes → RAG-augmented response
  - External app loses connectivity → falls back to Blaze API → recovers
  - Health check disables Azure Foundry → routes to Ollama instead
  - RAG doc uploaded via external app → syncs to cloud Blaze → queried by another client
  
- **Update documentation:**
  - Rewrite `Docs/summary/summary.md` with new architecture
  - Create `Docs/OFFLINE_MODE.md` (SDK setup for external apps, bundling, troubleshooting)
  - Create `Docs/RAG.md` (document upload, retrieval, sync)
  - Create `Docs/HEALTH_CHECKS.md` (monitoring, configuration)
  - Create `Docs/YARDLY_INTEGRATION.md` (example: how external apps use OfflineLlmGateway)
  
- **SDK example guide:**
  - Document how external apps (like Yardly) integrate OfflineLlmGateway.csproj
  - Example: plant identification workflow using local Gemma-4 + RAG

- **Quality gates:**
  - `dotnet build -warnaserror` on entire solution
  - 95% test coverage (including offline + RAG + health checks)
  - No new security vulnerabilities (SCA)

**Deliverable:** Full end-to-end offline + RAG + health checks working, documented, ready for external apps like Yardly to consume

---

## 3. Todo Tracking

**49 actionable todos tracked in SQL database with dependencies:**
- Phase 1: 4 critical bugs (independent)
- Phase 2: 6 health check tasks
- Phase 3: 8 RAG library tasks
- Phase 4: 10 offline SDK tasks
- Phase 5: 8 sync tasks
- Phase 6: 13 integration + docs + quality tasks

**Execution order:**
1. Phase 1 (blocker)
2. Phase 2 & 3 (parallel, independent)
3. Phase 4 (depends on 1 + 3)
4. Phase 5 (depends on 4)
5. Phase 6 (depends on all)

---

## 4. Architecture Diagram (ASCII)

```
┌─────────────────────────────────────────────────────────────────┐
│                    External App (e.g., Yardly)                 │
│                      Any .NET Platform                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │    OfflineGatewayService (from NuGet package)            │  │
│  │  - LocalModelRegistry (Gemma-4, router model)            │  │
│  │  - OfflineRoutingStrategy                                │  │
│  │  - LocalChatClient (ONNX Runtime / model inference)      │  │
│  │  - OfflineHealthMonitor                                  │  │
│  │  - RagRetrievalPipeline (SQLite FTS5)                    │  │
│  └──────────────────────────────────────────────────────────┘  │
│                           │                                     │
│                           ├─→ Local HTTP (http://localhost:...) │
│                           │                                     │
│                           └─→ Try API Fallback                  │
└─────────────────────────────────────────────────────────────────┘
                                     ▲
                                     │
                                     │ (hybrid mode)
                                     ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Blaze.LlmGateway API                        │
│                        (Server)                                  │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ ChatCompletionsEndpoint                                    │ │
│  │  1. Parse request (vision, tools, RAG hints)              │ │
│  │  2. Call RagRetrievalPipeline (if enabled)                │ │
│  │  3. Inject retrieved docs into context                    │ │
│  │  4. Route via LlmRoutingChatClient                         │ │
│  │  5. Stream response (SSE)                                 │ │
│  └────────────────────────────────────────────────────────────┘ │
│       │                                                          │
│       ├─→ ProviderHealthMonitor (async, non-blocking)           │
│       │   - Skips Down providers during routing                 │
│       │   - Lazy failure recovery                               │
│       │                                                          │
│       ├─→ RagRetrievalPipeline                                  │
│       │   - Query LocalSqliteDocumentStore (offline docs)       │
│       │   - Query CloudDocumentStore (shared docs)              │
│       │   - Inject into ChatOptions or system prompt            │
│       │                                                          │
│       └─→ LlmRoutingChatClient                                  │
│           - OllamaMetaRoutingStrategy (local router on device)  │
│           - KeywordRoutingStrategy (fallback)                   │
│           - Resolve keyed provider (AzureFoundry, Ollama, etc.) │
│           - Apply FunctionInvokingChatClient                    │
│                                                                  │
│  Endpoints:                                                      │
│  - GET  /health/models              ← Provider health status    │
│  - POST /v1/rag/sync                ← Device sync upload       │
│  - POST /v1/rag/documents           ← Upload doc               │
│  - GET  /v1/router-model            ← Download router model    │
│  - POST /v1/chat/completions        ← Main endpoint            │
│  - GET  /v1/models                  ← List available models    │
└──────────────────────────────────────────────────────────────────┘
```

---

## 5. Implementation Strategy

### Order of Execution
1. **Phase 1 first (bugs)**: Unblocks vision, function calling, GithubModels. Must complete before external apps can use the API.
2. **Phase 2 in parallel with Phase 3**: Health checks and RAG are independent. Can run in parallel.
3. **Phase 4 after 1 + 3**: Offline SDK depends on RAG (for document store) and bug fixes (for vision, etc.).
4. **Phase 5 after 4**: Sync only makes sense once offline SDK exists.
5. **Phase 6 at end**: Integration test everything together.

### Estimation Notes
- Phase 1: ~2 days (mostly wire-up, some MEAI adaptation for multimodal)
- Phase 2: ~3 days (async service, cache management, endpoint wiring)
- Phase 3: ~5 days (embedding service, SQLite FTS5, retrieval logic)
- Phase 4: ~6 days (model loaders, local routing, platform-specific paths)
- Phase 5: ~4 days (sync API, conflict resolution, encryption)
- Phase 6: ~3 days (e2e tests, docs, polish)

**Total: ~23 days of focused work**

---

## 6. Key Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Model bundling bloats app size | Use quantized models (int4), offer optional download |
| Local inference too slow on device | Test latency early, may need faster device or smaller model |
| RAG embedding cost in cloud | Offer local embedding option (AllMiniLm), cache embeddings |
| Health check false positives | Configurable probe strategy, graceful degradation |
| RAG sync race conditions | Last-write-wins + timestamp, idempotent API |
| Platform-specific issues | Test on real device early, not just emulator |

---

## 7. Acceptance Criteria

### By end of Phase 1 (Bug Fixes)
- [ ] Vision request (image URL in Content) passes through API end-to-end
- [ ] GithubModels is an available routing destination
- [ ] OpenAI client validates SSE chunks as spec-compliant
- [ ] Function calling tools are forwarded to model
- [ ] All tests pass, 95% coverage

### By end of Phase 3 (RAG)
- [ ] Upload a Yardly document (plant ID data)
- [ ] Query matches document, injected into context
- [ ] Offline mode has RAG working with SQLite
- [ ] Sync service background task runs

### By end of Phase 4 (Offline SDK)
- [ ] External app instantiates OfflineGateway
- [ ] Local router classifies a Gemma-4 request
- [ ] Offline mode falls back to API when unreachable
- [ ] Vision works end-to-end on device

### By end of Phase 6 (Full Integration)
- [ ] End-to-end: device offline → routes locally → RAG-augmented → syncs when online
- [ ] Health checks prevent routing to dead providers
- [ ] All critical paths have integration tests
- [ ] Docs updated, external app integration guide complete

---

## 8. Related Documentation

- `Docs/summary/summary.md` — Current state analysis (bugs, missing features, roadmap)
- `Docs/CLAUDE.md` — Architecture and conventions
- `Docs/design/adr/` — Architecture Decision Records (ADR-0008 cloud egress, ADR-0009 squad orchestration)
- `prompts/squad/` — Squad orchestration prompts (if using Conductor/Orchestrator)

---

## Next Steps

1. **Phase 1 (bugs)** — Start with critical bug fixes
2. **Get feedback** — Validate architecture with team
3. **Execute phases** — Follow SQL todo tracking via `status` field
4. **Documentation** — Update as you build each phase
5. **Quality gates** — Enforce `-warnaserror`, 95% coverage, no security issues
