# n8n Self-hosted AI Starter Kit (`n8n-io/self-hosted-ai-starter-kit`) — technical research report

**Repository:** [n8n-io/self-hosted-ai-starter-kit](https://github.com/n8n-io/self-hosted-ai-starter-kit)  
**Snapshot analyzed:** `9b802c62c609dedae5869ab2dfaf4a25daf817a1`  
**Research basis:** local mirrored source inspection under `research\sources\n8n-self-hosted-ai-starter-kit\repo` plus Blaze.LlmGateway repo guidance for integration fit.[^1][^2]

## Executive Summary

The starter kit is best understood as a **thin Docker Compose bootstrap**, not a standalone AI platform or reusable application framework.[^1][^3] Its whole value proposition is to stand up four cooperating building blocks quickly: **n8n** as the workflow/UI plane, **PostgreSQL** as n8n state storage, **Ollama** as the local model runtime, and **Qdrant** as the local vector store.[^1][^3]

The implementation is intentionally small. The repository mainly consists of `docker-compose.yml`, `.env.example`, and one seeded n8n demo workflow plus two encrypted credential stubs.[^2][^6][^7][^8] That seeded workflow is a very simple chat path — **Chat Trigger -> Basic LLM Chain -> Ollama Chat Model** — and it does **not** use Qdrant even though Qdrant is provisioned in the stack.[^6][^7][^8]

For Blaze.LlmGateway, the kit is promising as an **orchestration sandbox** but not as a ready-made integration surface.[^2][^3] Blaze already exposes an OpenAI-compatible streaming endpoint and routes across multiple providers, while the starter kit currently hard-wires its demo workflow to n8n’s Ollama chat node and only passes `OLLAMA_HOST` through Docker Compose, so a Blaze integration would require **changing n8n credentials/workflows** rather than merely flipping a compose variable.[^2][^3][^6][^7]

## Architecture / System Overview

```text
┌──────────────────────────────────────────────────────────┐
│ Browser / operator                                      │
│  - opens n8n UI on :5678                               │
└──────────────────────────────┬───────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────┐
│ n8n                                                     │
│  - stores state in Postgres                             │
│  - mounts /data/shared for local file workflows         │
│  - runs imported demo workflow                          │
└───────────────┬───────────────────────────────┬──────────┘
                │                               │
                ▼                               ▼
┌──────────────────────────┐        ┌──────────────────────┐
│ Ollama                   │        │ Qdrant               │
│  - local LLM runtime     │        │  - local vector DB   │
│  - pre-pulls llama3.2    │        │  - provisioned, not  │
│    through init job      │        │    used by demo flow │
└──────────────────────────┘        └──────────────────────┘
                │
                ▼
┌──────────────────────────┐
│ Postgres                 │
│  - n8n persistence       │
└──────────────────────────┘
```

At runtime, Docker Compose brings up Postgres first, waits for its health check, runs a one-shot `n8n-import` container to import demo credentials and workflows, then starts the main n8n service that exposes port `5678`; Ollama and its model-pull init job run under CPU, Nvidia GPU, or AMD GPU profiles, while Qdrant is always provisioned on port `6333`.[^3]

## Core Components

### 1. Docker Compose is the real product surface

The stack is defined almost entirely in `docker-compose.yml`, which declares persistent volumes for n8n, Postgres, Ollama, and Qdrant, plus a single shared `demo` network.[^3] The shared `x-n8n` anchor configures n8n to use Postgres, disables diagnostics and personalization, requires encryption and JWT secrets from `.env`, and injects `OLLAMA_HOST` with a default of `ollama:11434`.[^3]

That same compose file also shows the division of responsibilities very clearly:

1. `postgres` persists n8n state and gates startup through `pg_isready`.[^3]
2. `n8n-import` is a setup-only job that imports credentials and workflows from `./n8n/demo-data`.[^3]
3. `n8n` is the long-running UI/workflow container and mounts both `./n8n/demo-data` and `./shared` into the container.[^3]
4. `qdrant` is provisioned as a separate vector database service on `6333`.[^3]
5. `ollama-*` services provide CPU, Nvidia, or AMD-backed local inference, while companion `ollama-pull-llama-*` init containers pre-pull `llama3.2`.[^3]

This is important for Blaze planning because the starter kit has **no custom service layer of its own** where a gateway could be “plugged in”; the integration point is operational configuration plus n8n workflow design, not application code.[^3][^6]

### 2. Bootstrap behavior is demo-oriented, not lifecycle-heavy

The most opinionated logic in the whole repository is the `n8n-import` command block. It checks whether `n8n list:workflow --onlyId` returns anything, imports credentials and workflows only when the instance is empty, and otherwise prints `Workflows exist, skipping import`.[^3] That behavior aligns with the latest visible repository commit, `9b802c62c609dedae5869ab2dfaf4a25daf817a1`, whose message is **“Skip demo import if workflows already exist (#125)”**.[^9]

That recent maintenance pattern matters because it shows what the repo is optimizing for: **safe repeat startup of a seeded demo environment**, not an evolving application runtime with migrations, service code, or release automation.[^3][^9] The other recent commit messages visible from the repo history — updating the Basic LLM Chain version to avoid fallback-model issues, moving `.env`, and adding contributing guidance — reinforce that this repo is maintained as a template that tracks upstream n8n behavior, not as a feature-rich product in its own right.[^10]

### 3. The seeded workflow is intentionally minimal

The included workflow `srOnR8PAY3u4RSwb.json` contains only three nodes: a `Chat Trigger`, a `Basic LLM Chain`, and an `Ollama Chat Model` configured with `model: "llama3.2:latest"`.[^6] The workflow connections show the chat trigger feeding the chain and the Ollama model attached as the language model input for that chain.[^6]

That means the shipped starter experience is **pure prompt-in / response-out chat**, not a full agent with tools, memory, retrieval, or multi-model routing.[^6] Qdrant is provisioned in Docker Compose and a Qdrant credential is pre-seeded, but the default workflow JSON contains no Qdrant node at all, so retrieval is an available building block rather than part of the default happy path.[^3][^6][^8]

### 4. Credentials and configuration are pre-seeded but intentionally generic

The `.env.example` file contains placeholder values for `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, `N8N_ENCRYPTION_KEY`, and `N8N_USER_MANAGEMENT_JWT_SECRET`, and it defaults binary data storage to `filesystem`.[^4] The README repeatedly tells operators to copy `.env.example` to `.env` and update secrets/passwords before startup, which is another signal that this repo is a bootstrap convenience rather than a secure-by-default deployment package.[^1][^4]

Two encrypted credential stubs are bundled under `n8n/demo-data/credentials`: one named **Local Ollama service** of type `ollamaApi`, and one named **Local QdrantApi database** of type `qdrantApi`.[^7][^8] The `nodesAccess` metadata shows those credentials are meant for n8n’s Ollama model nodes and Qdrant vector-store node family, respectively.[^7][^8]

### 5. Operator experience is local-first

The README’s install matrix is all about local hardware choices: CPU, Nvidia GPU, AMD GPU, or Mac with local Ollama outside Docker.[^1] For Mac users running Ollama locally, the documented flow is to set `OLLAMA_HOST=host.docker.internal:11434` in `.env` and then manually update the **Local Ollama service** credential in the n8n UI to use `http://host.docker.internal:11434/`.[^1][^4][^7]

The repo also creates and mounts a `shared` folder so n8n workflows can reach host files through `/data/shared` inside the n8n container, and the README explicitly calls out file-system-aware nodes such as Read/Write Files, Local File Trigger, and Execute Command.[^1][^3] That local-file orientation is useful for Blaze experimentation because it makes the starter kit a practical place to prototype document ingestion, sidecar commands, or local trace export workflows without adding more infrastructure first.[^1][^3]

## Implications for Blaze.LlmGateway integration

Blaze.LlmGateway already exposes an OpenAI-compatible `POST /v1/chat/completions` streaming endpoint and routes requests across multiple keyed providers, including Ollama, Azure Foundry, GitHub Copilot, Gemini, OpenRouter, and related variants.[^2] In contrast, the starter kit’s demo path binds n8n directly to an Ollama-specific credential and node type, with `OLLAMA_HOST` as the only model-endpoint variable surfaced by Compose.[^3][^6][^7]

That leads to three practical conclusions:

1. **Blaze should be introduced as a model-backend replacement or addition, not as a replacement for n8n itself.** The starter kit already uses n8n as the orchestration/UI layer and Postgres as its state layer.[^2][^3]
2. **Compose-only integration is insufficient.** Because the shipped workflow explicitly references `@n8n/n8n-nodes-langchain.lmChatOllama` and the bundled `ollamaApi` credential, integrating Blaze requires editing workflow nodes and credentials, not just changing `.env`.[^3][^6][^7]
3. **The best fit is proof-of-concept validation first.** The README explicitly says the starter kit is not fully optimized for production and is intended to help users get started with proof-of-concept self-hosted AI workflows.[^1]

A sensible Blaze integration strategy would therefore be:

1. Keep **n8n + Postgres + shared storage** as the orchestration shell.[^1][^3]
2. Treat **Blaze** as the model-routing control plane that can sit where the current workflow assumes a single Ollama backend.[^2][^6]
3. Keep **Qdrant** local for retrieval experiments when you move beyond the seeded chat-only workflow, since the stack already provisions it even though the default workflow does not consume it.[^3][^8]

## Key Files Summary

| File | Purpose | Why it matters |
|---|---|---|
| `docker-compose.yml` | Defines all services, volumes, profiles, init jobs, and startup ordering | This is the actual system architecture.[^3] |
| `.env.example` | Defines the minimum operator-supplied secrets and runtime knobs | Shows the starter kit’s security/configuration assumptions.[^4] |
| `n8n/demo-data/workflows/srOnR8PAY3u4RSwb.json` | Seeded demo chat workflow | Reveals the real default AI path and its limitations.[^6] |
| `n8n/demo-data/credentials/xHuYe0MDGOs9IpBW.json` | Seeded Ollama credential | Shows the workflow is wired to Ollama-specific node types.[^7] |
| `n8n/demo-data/credentials/sFfERYppMeBnFNeA.json` | Seeded Qdrant credential | Shows retrieval is available as a preconfigured capability even if unused by the demo flow.[^8] |
| `.github/copilot-instructions.md` in Blaze.LlmGateway | Documents Blaze’s gateway surface and provider topology | Establishes why Blaze is a good backend candidate for this starter kit.[^2] |

## Confidence Assessment

**High confidence** on the starter kit’s architecture, startup behavior, seeded workflow, and Blaze integration implications that follow directly from the files studied. Those conclusions are grounded in the compose file, env template, bundled workflow JSON, credential JSON, and Blaze’s own repo guidance.[^2][^3][^4][^6][^7][^8]

**Medium confidence** on the exact best n8n-side node strategy for Blaze. The starter kit repo itself does not ship a Blaze-aware workflow or a generic model-gateway abstraction, so any deeper integration design beyond “replace the direct Ollama dependency with a Blaze-facing workflow/credential pattern” requires additional n8n-node-specific research outside this repository.[^3][^6][^7]

## Footnotes

[^1]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\README.md:3-26,38-46,72-141,208-218`.
[^2]: `C:\src\CodebrewRouter\.github\copilot-instructions.md:33-58,62-72`.
[^3]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\docker-compose.yml:1-156`.
[^4]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\.env.example:1-11`.
[^5]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\README.md:113-141`.
[^6]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\n8n\demo-data\workflows\srOnR8PAY3u4RSwb.json:1-87`.
[^7]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\n8n\demo-data\credentials\xHuYe0MDGOs9IpBW.json:1-18`.
[^8]: Snapshot `9b802c62c609dedae5869ab2dfaf4a25daf817a1`. `research\sources\n8n-self-hosted-ai-starter-kit\repo\n8n\demo-data\credentials\sFfERYppMeBnFNeA.json:1-14`.
[^9]: GitHub commit `9b802c62c609dedae5869ab2dfaf4a25daf817a1` in [n8n-io/self-hosted-ai-starter-kit](https://github.com/n8n-io/self-hosted-ai-starter-kit) (“Skip demo import if workflows already exist (#125)”).
[^10]: GitHub commits in [n8n-io/self-hosted-ai-starter-kit](https://github.com/n8n-io/self-hosted-ai-starter-kit): `9ccbaf42e007369453acaec60b9808cf8e6cdb44` (“Use latest version of Basic LLM Chain, to avoid fallback model issues (#96)”), `06319a57af662810230c1a63175baaf312b427a9` (“Move \`.env\` file and simplify setup on mac (#68)”), and `cad1f5454fcb66819fae845ff8d267dde7aa69b6` (“Add contributing document (#84)”).
