# LiteLLM Research Report

Repository: [BerriAI/litellm](sources/berriai-litellm/repo/README.md)

## Executive Summary

LiteLLM is two products in one repository: a Python SDK that normalizes calls to many LLM providers into an OpenAI-like API, and a FastAPI-based AI gateway that centralizes auth, spend controls, routing, and admin workflows behind OpenAI-compatible endpoints.[^1][^2][^3] The current package version is `1.83.8`, with a small core dependency set for the SDK and a much larger `proxy`/`proxy-runtime` surface for the gateway, including FastAPI, APScheduler, Prisma-backed features, observability libraries, and extra workspace packages.[^3][^4][^5][^6]

Architecturally, the SDK path centers on `litellm.main` and `litellm.Router`: `completion()` / `acompletion()` provide the unified request API, while `Router` adds multi-deployment routing, retries, cooldowns, health-aware filtering, and fallbacks.[^7][^8][^9][^10][^11][^12] The gateway path centers on `litellm/proxy/proxy_server.py`, which boots a `FastAPI` app with a lifespan hook, loads `LITELLM_MASTER_KEY` and YAML config, exposes OpenAI-compatible routes like `/v1/chat/completions` and `/v1/embeddings`, and funnels request execution through a shared request processor that also stamps spend, model, and rate-limit metadata into response headers.[^13][^14][^15][^16][^17][^18][^19]

LiteLLM's admin plane is not bolted on as a thin wrapper; it is a substantial subsystem. Virtual-key generation, team/user/project scoping, model allowlists, route allowlists, per-key RPM/TPM/parallelism, budgets, prompt restrictions, and router overrides are first-class concepts in the key-management and auth layers, while spend logging and budgeting are persisted and scheduled as background jobs.[^20][^21][^22][^23][^24][^25][^26][^27][^28][^29][^30][^31]

Operationally, the repo shows a strong bias toward self-hosting and hardening. The quick-start docs emphasize `litellm --setup` for local onboarding, `config.yaml` model aliasing for provider routing, a stateless Docker image for basic proxying, and a database-backed image when generating keys/users/teams.[^32][^33] The hardened Docker path builds the admin UI into the image, runs as `nobody`, supports read-only-rootfs patterns, caches Prisma assets for offline behavior, and runs a migration entrypoint before startup.[^34][^35][^36][^37]

## Architecture / System Overview

```text
                    ┌──────────────────────────┐
                    │ Client / SDK Consumer    │
                    │ OpenAI-style request     │
                    └────────────┬─────────────┘
                                 │
                  Python SDK     │     Gateway / Proxy
                                 │
        ┌────────────────────────▼────────────────────────┐
        │              LiteLLM unified interface          │
        │  litellm.main: completion() / acompletion()    │
        └────────────┬───────────────────────┬────────────┘
                     │                       │
                     │ direct SDK use        │ proxy call
                     ▼                       ▼
          ┌───────────────────┐   ┌────────────────────────────┐
          │ Router            │   │ FastAPI proxy_server       │
          │ - model groups    │   │ - auth + virtual keys      │
          │ - retries         │   │ - OpenAI-compatible routes │
          │ - fallbacks       │   │ - common request processor │
          │ - cooldown/health │   │ - spend / budgets / admin  │
          └─────────┬─────────┘   └─────────────┬──────────────┘
                    │                           │
                    └──────────────┬────────────┘
                                   ▼
                    ┌──────────────────────────┐
                    │ Provider adapters        │
                    │ OpenAI / Azure / AWS /   │
                    │ Anthropic / Gemini / ... │
                    └──────────────────────────┘
```

At a high level, LiteLLM's public API promises a single OpenAI-style interface, while the implementation splits into an in-process library path and a server path. The README explicitly frames that split as "LiteLLM AI Gateway" vs. "LiteLLM Python SDK", and the package metadata mirrors it with separate core, `proxy`, and `proxy-runtime` dependency sets plus workspace members for `enterprise` and `litellm-proxy-extras`.[^2][^3][^4][^5][^6]

## 1. Python SDK and Router

The SDK entrypoints are `completion()` and `acompletion()` in `litellm/main.py`. Both accept a provider-qualified `model`, OpenAI-style `messages`, streaming/tool parameters, provider connection parameters (`base_url`, `api_key`, `api_version`), and LiteLLM-specific controls like `model_list` and schema validation overrides.[^7][^8] This is the core "uniform request shape" promise of the library.

Retries exist in the SDK itself, but the implementation steers users toward router-based retries for async flows: `completion_with_retries()` and `acompletion_with_retries()` wrap calls with Tenacity and explicitly zero out the underlying per-call retry knobs; the async helper is marked deprecated in favor of `acompletion` or `router.acompletion`.[^9]

`Router` is where LiteLLM becomes an orchestration layer rather than a thin adapter. Its constructor accepts deployment lists, Redis/cache settings, retry and fallback controls, cooldown policy, pre-call checks, tag filtering, budget config, and a named routing strategy (`simple-shuffle`, `least-busy`, `usage-based-routing`, `latency-based-routing`, `cost-based-routing`, and `usage-based-routing-v2`).[^10] That matches the README's positioning of Router as application-level load balancing with retry/fallback logic across multiple deployments.[^2]

The runtime behavior also matches that contract. `async_function_with_fallbacks()` first tries the retry path, then delegates exceptions into shared fallback handling when retries are exhausted.[^11] Deployment choice is made in `get_available_deployment()`, which first performs common eligibility checks, then filters by health-check status, then cooldown state, then optional pre-call checks and explicit deployment ordering before finally applying the selected routing strategy; if no candidate remains, it raises `RouterRateLimitError` with cooldown context.[^12] In practice, Router is LiteLLM's policy engine for "which backend gets this call right now?" rather than just a config holder.[^10][^11][^12]

## 2. Gateway / Proxy Request Path

The gateway is a `FastAPI` application created in `proxy_server.py`, using a lifespan hook (`proxy_startup_event`) rather than ad hoc startup functions.[^14] During startup, the proxy initializes verbose loggers, executes optional worker startup hooks, reads `LITELLM_MASTER_KEY`, and loads YAML config from `CONFIG_FILE_PATH` or `WORKER_CONFIG` into `llm_router`, `llm_model_list`, and `general_settings`.[^13] There is also a CLI-facing `initialize()` path that can load config directly and set model/debug/runtime defaults, which is the bridge between CLI startup and the gateway's server state.[^15]

The main chat surface is explicitly OpenAI-compatible. `proxy_server.py` registers `/v1/chat/completions`, `/chat/completions`, and Azure-style deployment aliases, all guarded by `user_api_key_auth`, and the handler feeds request data plus authenticated metadata into `ProxyBaseLLMRequestProcessing.base_process_llm_request()`.[^16][^18] That same file also exposes OpenAI-style embeddings, realtime websocket endpoints, and an experimental queued chat-completions path, so the proxy is broader than a single synchronous chat endpoint.[^31]

The common request processor is a key architectural seam. `ProxyBaseLLMRequestProcessing` centralizes request execution and response decoration across chat, embeddings, responses, files, batches, realtime, vector stores, OCR/search/video, containers, and other route types.[^18] It also emits rich response headers such as LiteLLM call ID, model ID, cache key, model region, response cost, key TPM/RPM limits, key spend, and request timing.[^17] Another subtle but important behavior is model restamping: LiteLLM tries to keep the OpenAI `model` field aligned with the client-requested alias unless a real fallback occurred, the request went through Azure Model Router semantics, or a fastest-response batch selected a different winner.[^19]

## 3. Auth, Virtual Keys, and Policy Enforcement

The auth layer is gateway-native, not provider-native. `user_api_key_auth()` reads and normalizes the request body, determines the request route, builds an auth object from headers/JWT/custom headers, applies route-level checks, extracts the end-user identifier, and returns a normalized `UserAPIKeyAuth` object consumed downstream by the proxy.[^22] Returned auth objects may carry user-level RPM/TPM and budget state, not just an opaque token identifier.[^22]

Model access enforcement is also layered. `_enforce_key_and_fallback_model_access()` checks the requested model and any client-supplied fallback models against the authenticated key's effective permissions and the active router/model list.[^23] That means LiteLLM does not just authorize the primary target; it also validates fallback targets before they can be exercised.[^23]

Virtual keys are a first-class API. `/key/generate` is an authenticated management endpoint returning `GenerateKeyResponse`.[^20] The implementation supports an unusually large policy surface: expiry duration, aliases, team/user/agent/org/project attachment, model allowlists, config overrides, spend counters, max budget, budget duration, max parallel requests, permissions, guardrails, model-specific budgets and RPM/TPM, soft budget, route allowlists, passthrough allowlists, key type, rotation settings, vector-store access, router settings, and access groups.[^21] This is the clearest sign that LiteLLM's gateway is intended to act as a multi-tenant control plane rather than a mere reverse proxy.[^20][^21]

The Docker quick-start aligns with the code. The docs tell operators to create a master key (admin credential), wire `general_settings.database_url` for Postgres-backed key/user/team management, start the database-backed image, create a key with `rpm_limit`, and observe the second request fail with a 429 once the key's RPM is exceeded.[^33] The endpoint name in the docs matches the actual implementation path in `key_management_endpoints.py`.[^20][^33]

## 4. Spend Tracking and Cost Accounting

LiteLLM's cost model is adapter-aware. `cost_per_token()` dispatches pricing logic across provider-specific calculators (OpenAI, Anthropic, Bedrock, Azure OpenAI, Gemini, DeepSeek, Perplexity, xAI, and others), while `completion_cost()` computes the final dollar amount from a response object, model metadata, service tier, and optional custom pricing.[^24][^25] `response_cost_calculator()` is the top-level convenience entrypoint: it prefers provider-supplied hidden cost data when available, otherwise falls back to `completion_cost()`.[^26]

Spend logging is tightly integrated with proxy auth and request metadata. The spend payload hashes `sk-` keys before persistence, optionally replaces master-key hashes with a fixed alias when configured not to store them, and records user/team/org IDs, request tags, model group/model ID, agent ID, API base, request/response bodies, session ID, duration, and the computed `response_cost`.[^27][^28] Prompt/response persistence itself is gated by `general_settings.store_prompts_in_spend_logs`, so operators can trade off analytics richness against data-retention sensitivity.[^29]

On the admin side, the proxy schedules spend updates and daily tag-spend aggregation as background jobs rather than doing all accounting inline on request paths.[^30] That design choice helps explain how LiteLLM can expose fine-grained spend controls without forcing every management calculation into the hot path.[^28][^30]

## 5. Health Checks and Background Operations

Router behavior and proxy operations both depend on health signals. `perform_health_check()` filters by model or deployment ID, de-duplicates alias collisions, runs bounded-concurrency checks, and returns healthy/unhealthy endpoint sets plus exceptions keyed by model ID.[^31] On the routing side, `Router.get_available_deployment()` consumes health-check filtering before cooldown and retry logic, so health is part of actual traffic steering, not just observability.[^12][^31]

The proxy also spins up scheduled jobs during initialization. `initialize_scheduled_background_jobs()` configures an APScheduler instance with memory-oriented settings intended to mitigate prior memory leaks, then schedules budget resets, spend updates, tag-spend aggregation, and spend-log queue monitoring.[^30] This is an important operational clue: LiteLLM's gateway expects to be a long-running service with periodic maintenance work, not a stateless shim that only forwards HTTP calls.[^13][^30]

The test suite reinforces that operational contract. `test_basic_proxy_startup.py` asserts readiness/liveness endpoints and a real `/chat/completions` request against a running server, while `test_router_auto_router.py` demonstrates Router being used as an intent-driven selector over an `auto_router/...` model group backed by multiple candidate models.[^38][^39]

## 6. Docker and the Added Quick-Start Research

The repository's main README points new gateway users to the end-to-end Docker quick-start and shows the minimal local install flow with `uv tool install 'litellm[proxy]'` and a `litellm` CLI invocation.[^32] The quick-start page then expands that into two onboarding modes:

1. **Beginner/local wizard flow:** run the install script or `litellm --setup`, choose providers, enter API keys, set port and master key, and persist a `litellm_config.yaml` that can immediately start the proxy.[^33]
2. **Config-driven Docker flow:** mount `litellm_config.yaml`, pass provider secrets as env vars, and start the stateless image `docker.litellm.ai/berriai/litellm:main-latest`; for virtual keys and user/team management, add Postgres-backed `general_settings.database_url` and use `ghcr.io/berriai/litellm-database:main-latest` instead.[^33]

The docs' model alias explanation also matches the proxy/runtime design: clients call a friendly `model_name` like `gpt-4o`, while `litellm_params.model` uses a `<provider>/<model-identifier>` string that tells LiteLLM which backend adapter to invoke and what downstream model/deployment name to send.[^33] That aliasing model lines up with the proxy startup config loading and the Router's model-group/deployment selection model.[^10][^13]

The hardened Docker path in-repo goes further than the quick-start. `Dockerfile.non_root` prebuilds the admin UI into `/var/lib/litellm/ui`, copies static assets, runs `prisma generate`, sets offline/cache-related Prisma and npm env vars, fixes ownership/permissions for runtime assets, then drops to `USER nobody` before exposing port 4000 and launching `prod_entrypoint.sh`.[^34][^35] The Docker README explicitly describes this as a non-root, read-only-rootfs-oriented deployment with tmpfs-backed writable paths and restricted egress validation.[^36] Meanwhile, `docker/entrypoint.sh` runs the Prisma migration script before the service starts, confirming that container startup includes schema/bootstrap work, not just process launch.[^37]

## Key Repositories Summary

| Repository | Purpose | Key files |
|---|---|---|
| [BerriAI/litellm](sources/berriai-litellm/repo/README.md) | Monorepo for the Python SDK, FastAPI AI gateway, Docker packaging, and workspace extensions (`enterprise`, `litellm-proxy-extras`).[^3][^4][^5][^6] | `litellm/main.py`, `litellm/router.py`, `litellm/proxy/proxy_server.py`, `litellm/proxy/auth/user_api_key_auth.py`, `litellm/proxy/management_endpoints/key_management_endpoints.py`, `litellm/proxy/spend_tracking/spend_tracking_utils.py`, `docker/Dockerfile.non_root` |

## Confidence Assessment

**High confidence:** The SDK/gateway split, router responsibilities, OpenAI-compatible proxy surfaces, auth/key-management scope, spend-accounting model, scheduled background jobs, Docker hardening posture, and the quick-start's stateless-vs-database-backed deployment distinction are all directly verified in source or docs.[^2][^3][^10][^13][^16][^20][^21][^28][^30][^33][^34][^36]

**Medium confidence / bounded inference:** I did not inspect every provider adapter or every endpoint module in the repository, so statements about "all providers" are based on the repo's explicit public claims plus representative core files rather than a full census of each adapter implementation.[^1][^2][^24] I also treated the quick-start page as current operator guidance, but I did not independently execute its commands in this environment.[^33]

## Footnotes

[^1]: `sources/berriai-litellm/repo/README.md:46-61` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^2]: `sources/berriai-litellm/repo/README.md:374-403` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^3]: `sources/berriai-litellm/repo/pyproject.toml:1-40` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^4]: `sources/berriai-litellm/repo/pyproject.toml:35-60` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^5]: `sources/berriai-litellm/repo/pyproject.toml:89-115` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^6]: `sources/berriai-litellm/repo/pyproject.toml:216-221` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^7]: `sources/berriai-litellm/repo/litellm_main.py:378-455` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^8]: `sources/berriai-litellm/repo/litellm_main.py:1051-1128` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^9]: `sources/berriai-litellm/repo/litellm_main.py:4420-4479` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^10]: `sources/berriai-litellm/repo/litellm_router.py:216-340` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^11]: `sources/berriai-litellm/repo/litellm_router.py:5573-5623` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^12]: `sources/berriai-litellm/repo/litellm_router.py:9650-9735` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^13]: `sources/berriai-litellm/repo/proxy_server.py:769-850` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^14]: `sources/berriai-litellm/repo/proxy_server.py:998-1007` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^15]: `sources/berriai-litellm/repo/proxy_server.py:5629-5750` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^16]: `sources/berriai-litellm/repo/proxy_server.py:7113-7218` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^17]: `sources/berriai-litellm/repo/common_request_processing.py:489-575` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^18]: `sources/berriai-litellm/repo/common_request_processing.py:879-985` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^19]: `sources/berriai-litellm/repo/common_request_processing.py:332-368` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^20]: `sources/berriai-litellm/repo/key_management_endpoints.py:1156-1168` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^21]: `sources/berriai-litellm/repo/key_management_endpoints.py:1163-1265` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^22]: `sources/berriai-litellm/repo/user_api_key_auth.py:1574-1688` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^23]: `sources/berriai-litellm/repo/user_api_key_auth.py:1790-1843` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^24]: `sources/berriai-litellm/repo/cost_calculator.py:260-340` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^25]: `sources/berriai-litellm/repo/cost_calculator.py:1016-1118` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^26]: `sources/berriai-litellm/repo/cost_calculator.py:1677-1759` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^27]: `sources/berriai-litellm/repo/spend_tracking_utils.py:273-330` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^28]: `sources/berriai-litellm/repo/spend_tracking_utils.py:430-490` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^29]: `sources/berriai-litellm/repo/spend_tracking_utils.py:904-916` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^30]: `sources/berriai-litellm/repo/proxy_server.py:6256-6368` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^31]: `sources/berriai-litellm/repo/health_check.py:361-474` (commit `0b7335201b22e27e14ea836455444e19a768492e`); `sources/berriai-litellm/repo/proxy_server.py:7510-7565` (commit `0b7335201b22e27e14ea836455444e19a768492e`); `sources/berriai-litellm/repo/proxy_server.py:8113-8165` (commit `0b7335201b22e27e14ea836455444e19a768492e`); `sources/berriai-litellm/repo/proxy_server.py:11337-11405` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^32]: `sources/berriai-litellm/repo/README.md:104-115` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^33]: `sources/berriai-litellm/repo/docker_quick_start.html:37-57` (source `sources/berriai-litellm/repo/docker_quick_start.html`); `sources/berriai-litellm/repo/docker_quick_start.html:85-109` (source `sources/berriai-litellm/repo/docker_quick_start.html`); `sources/berriai-litellm/repo/docker_quick_start.html:120-180` (source `sources/berriai-litellm/repo/docker_quick_start.html`); `sources/berriai-litellm/repo/docker_quick_start.html:180-209` (source `sources/berriai-litellm/repo/docker_quick_start.html`)
[^34]: `sources/berriai-litellm/repo/Dockerfile.non_root:65-130` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^35]: `sources/berriai-litellm/repo/Dockerfile.non_root:168-212` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^36]: `sources/berriai-litellm/repo/docker_README.md:64-84` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^37]: `sources/berriai-litellm/repo/entrypoint.sh:1-16` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^38]: `sources/berriai-litellm/repo/test_basic_proxy_startup.py:1-54` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
[^39]: `sources/berriai-litellm/repo/test_router_auto_router.py:19-100` (commit `0b7335201b22e27e14ea836455444e19a768492e`)
