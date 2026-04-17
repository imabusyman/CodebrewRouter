# Microsoft Foundry Local (`microsoft/Foundry-Local`) — technical research report

**Repository:** [microsoft/Foundry-Local](https://github.com/microsoft/Foundry-Local)  
**Local mirror:** [sources/microsoft-foundry-local/repo/README.md](sources/microsoft-foundry-local/repo/README.md)  
**Snapshot analyzed:** `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`  
**Research basis:** local source inspection of the mirrored repository under `research\sources\microsoft-foundry-local\repo`.

## Executive Summary

`microsoft/Foundry-Local` is an end-to-end on-device AI stack for shipping local inference inside applications, not just a CLI wrapper or a dev server. The repo positions the product around multi-language SDKs, a curated local model catalog, automatic hardware acceleration, and an optional OpenAI-compatible web service for tooling and multi-process scenarios.[^1]

The most important architectural finding is that the language SDKs are thin orchestration layers over a shared packaged native runtime. JavaScript, Python, .NET, Rust, and the C++ wrapper all converge on command-style calls into `Microsoft.AI.Foundry.Local.Core`, while separately arranging ONNX Runtime and ONNX Runtime GenAI native dependencies for the current platform.[^2]

For practical adoption, Foundry Local gives you two integration modes. You can embed it directly through language SDKs to manage catalog lookup, execution provider bootstrapping, download/load/unload, and native chat/audio clients, or you can start the local web service and treat it like an OpenAI-compatible endpoint from OpenAI SDKs, LangChain, or even the GitHub Copilot SDK.[^3]

## Architecture / System Overview

At a high level, the repo is organized around four product surfaces: SDKs under `sdk/`, hands-on integrations under `samples/`, lightweight documentation routing under `docs/`, and a separate SvelteKit marketing/catalog site under `www/`.[^4]

The runtime shape looks like this:

```text
Application code
    |
    +--> Foundry Local SDK (JS / Python / .NET / Rust / C++)
            |
            +--> Manager + Catalog + Model abstractions
                    |
                    +--> CoreInterop / FFI bridge
                            |
                            +--> Microsoft.AI.Foundry.Local.Core
                                    +--> model catalog
                                    +--> EP discovery / registration
                                    +--> model download / cache / load / unload
                                    +--> chat + audio inference
                                    +--> optional local web service
                                            |
                                            +--> OpenAI-compatible REST + SSE clients
```

That layering is not accidental. The manager types in every language own initialization, catalog access, EP registration, and web-service start/stop, while model types expose download/load/unload plus factory methods for chat/audio clients. The same concepts recur across JS, Python, .NET, Rust, and the C++ wrapper, which makes the repo feel like one product surface with five language adapters rather than five unrelated SDKs.[^5]

## Core Runtime and Language Bindings

The shared native-core design is the defining implementation choice in this repo:

| Language surface | How it reaches the core | Packaging / runtime notes |
| --- | --- | --- |
| JavaScript | Node-API addon + dynamic core library loading | Loads `foundry_local_napi.node`, then resolves `Microsoft.AI.Foundry.Local.Core` and sibling ORT/GenAI binaries; auto-enables WinML bootstrap when the bootstrap DLL is present.[^6] |
| Python | `ctypes` FFI | Discovers `foundry-local-core`, `onnxruntime-*`, and `onnxruntime-genai-*` packages, preloads them, then calls the same `initialize` and command APIs.[^7] |
| .NET | `LibraryImport` / `DllImport` resolver | Resolves `Microsoft.AI.Foundry.Local.Core` from publish output or `runtimes/<rid>/native`, manually preloads ORT DLLs on Windows, and references `Microsoft.AI.Foundry.Local.Core` NuGet packages from the SDK project.[^8] |
| Rust | `libloading` + build script | Uses `build.rs` to fetch pinned native packages for the active RID, extracts the native libraries, then binds `execute_command` and `execute_command_with_callback` with `libloading`.[^9] |
| C++ | Direct wrapper over the same core | The wrapper constructs `Core` and sends named commands such as `initialize`, `get_model_list`, `download_model`, `chat_completions`, and `audio_transcribe`; today the C++ SDK is Windows-only.[^10] |

The implication is important: most of the product's "real" runtime behavior lives behind the native `Microsoft.AI.Foundry.Local.Core` boundary, not inside the JS/Python/.NET/Rust business logic. The checked-in language code mostly validates inputs, marshals requests, manages model state, and normalizes platform packaging; inference, hardware selection, model execution, and service hosting happen behind native command calls.[^2]

## Catalog, Execution Providers, and Model Lifecycle

Model discovery is intentionally cached. The C++, JavaScript, Python, and Rust catalogs all refresh from `get_model_list`, keep a six-hour cache, and rebuild alias-to-model / id-to-variant indexes. The manager EP download path invalidates that cache so newly registered execution providers can surface additional models on the next lookup.[^11]

The catalog data model is richer than just model IDs. `ModelInfo` includes runtime metadata such as device type / execution provider, cache state, file size, context length, input/output modalities, capability tags, and `supportsToolCalling`. Model aliases group multiple variants, and the SDKs prefer a locally cached variant when selecting the current default for a grouped model.[^12]

Lifecycle operations are consistent across languages: download, get path, load into memory, unload, remove from cache, and create chat/audio clients from the selected variant. The manager layer also exposes discover/download/register for execution providers, with progress callbacks shaped consistently as `(provider, percent)` across JS, Python, .NET, and Rust.[^13]

## OpenAI-Compatible Surfaces

Foundry Local offers two related but distinct OpenAI-shaped surfaces. First, the in-process SDK chat/audio clients serialize OpenAI-style request bodies and send them to native commands like `chat_completions` and `audio_transcribe`. Those clients also expose Foundry-specific extensions such as `top_k` and `random_seed` through metadata, plus structured output and tool-choice controls.[^14]

Second, the optional web service exposes HTTP APIs so generic ecosystem clients can connect over REST instead of FFI. In JavaScript, `FoundryLocalManager.createResponsesClient()` requires the service to be running, and the `ResponsesClient` talks to `/v1/responses` with standard JSON plus SSE streaming. That makes the Responses API explicitly HTTP-only even though chat/audio also exist as native SDK clients.[^15]

One subtle implementation detail is that the repo shows two OpenAI-style path conventions. Samples in JS, Python, and Rust use `/v1/chat/completions`, while a .NET XML doc comment still documents `/v1/chat_completions` plus `/v1/models` endpoints. I would treat the slash form as the current practical contract because it is what the runnable samples actually target, but this is a real doc/source inconsistency worth watching.[^16]

The SDKs also support a split-process mode. Both the JS and Python configurations accept an external service URL, and their model-load managers switch from direct native commands to HTTP routes like `/models/load/{id}`, `/models/unload/{id}`, and `/models/loaded` when that external URL is configured. That is effectively a built-in remote-control path for the local service.[^17]

## Integration Examples in Practice

The repo does a good job showing how Foundry Local is meant to be used in real applications instead of toy SDK snippets:

1. **Standard OpenAI SDK clients**: the web-server samples start Foundry Local, load a model, then point either the JavaScript OpenAI client or Python `openai.OpenAI(...)` at `http://localhost/.../v1` with a placeholder API key.[^18]
2. **LangChain**: the JS LangChain sample configures `ChatOpenAI` with a local Foundry Local base URL and uses it as a normal chain component.[^19]
3. **GitHub Copilot SDK**: the Copilot sample uses Foundry Local as a BYOK OpenAI provider for local tool-using sessions, and the companion sample adds custom tools on top of that local endpoint.[^20]

That ecosystem story is probably the strongest adoption signal in the repo. Microsoft is not asking consumers to learn a proprietary wire protocol first; it is leaning hard on OpenAI-shaped compatibility so existing clients and agent frameworks can be retargeted to a local endpoint with minimal code churn.[^1]

## Practical Fit for Blaze.LlmGateway

For a gateway like Blaze.LlmGateway, the shortest path is the web-service mode, not embedding the SDK. Microsoft's own samples already treat Foundry Local as a generic OpenAI-compatible backend for OpenAI SDKs, LangChain, and Copilot SDK, which aligns well with a gateway that already knows how to talk to OpenAI-shaped providers.[^18][^19][^20]

If you ever wanted richer local-device control, the embedded SDK path exposes capabilities a plain OpenAI endpoint does not: execution-provider discovery/registration, direct model download/load/unload, catalog inspection, and split-process service control. The tradeoff is operational complexity, because each language binding must carry the packaged native core plus ONNX dependencies for the target runtime.[^6][^7][^8][^9][^13][^17]

## Confidence Assessment

**High confidence**

- The repo clearly provides a multi-language SDK family, a packaged native-core architecture, execution-provider management, model lifecycle APIs, and an optional OpenAI-compatible web service.[^1][^2][^5][^11][^13][^15]
- The repo also clearly shows real integrations with generic OpenAI clients, LangChain, and the GitHub Copilot SDK.[^18][^19][^20]

**Moderate confidence / inferred boundaries**

- The exact internals of hardware selection, inference scheduling, KV-cache behavior, and service implementation are only partially visible, because the language SDKs delegate to the packaged `Microsoft.AI.Foundry.Local.Core` binary rather than reimplementing those behaviors in source.[^2][^6][^7][^8][^9]
- The CLI is documented in the root README, but this snapshot's engineering center of gravity is the SDK/binding/sample stack. I did not rely on any unstated claim about where the CLI implementation lives beyond what the repo explicitly documents.[^1][^4]

## Footnotes

[^1]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\README.md:20-36,161-214,220-236` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^2]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\foundry_local_manager.cpp:50-189`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\model.cpp:34-141`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\detail\coreInterop.ts:20-43,57-109`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\detail\core_interop.py:112-159,188-225`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\Detail\CoreInterop.cs:18-52,121-146`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\detail\core_interop.rs:223-317` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^3]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\web-server-example\app.js:13-18,51-73`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\python\web-server\src\app.py:40-67`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\rust\foundry-local-webserver\src\main.rs:69-118`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\langchain-integration-example\app.js:56-97`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\copilot-sdk-foundry-local\src\app.ts:83-127` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^4]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\docs\README.md:1-30`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\README.md:1-15`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\www\README.md:1-23,59-79` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^5]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\foundryLocalManager.ts:8-26,48-97,99-229`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\foundry_local_manager.py:27-78,79-196`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\FoundryLocalManager.cs:35-45,94-225,293-427`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\foundry_local_manager.rs:23-92,104-225`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\foundry_local_manager.cpp:20-48,94-189` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^6]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\detail\coreInterop.ts:20-43,57-109`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\package.json:8-27` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^7]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\detail\core_interop.py:112-159,188-225`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\requirements.txt:1-9`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\pyproject.toml:6-18,35-48` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^8]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\Detail\CoreInterop.cs:18-52,54-119,121-173`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\Microsoft.AI.Foundry.Local.csproj:16-18,35-37,85-133` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^9]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\build.rs:10-18,75-149,189-386`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\detail\core_interop.rs:223-317` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^10]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\CMakeLists.txt:15-28,52-58`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\include\foundry_local.h:7-18`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\foundry_local_manager.cpp:102-189`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\openai_chat_client.cpp:95-138`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\openai_audio_client.cpp:25-60` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^11]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\catalog.cpp:71-129`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\catalog.ts:35-75`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\catalog.py:54-90`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\catalog.rs:15-17,90-123,230-250`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\foundryLocalManager.ts:206-212`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\foundry_local_manager.py:157-162`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\FoundryLocalManager.cs:419-424`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\foundry_local_manager.rs:216-222` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^12]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\detail\model_data_types.py:12-18,30-35,50-83`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\types.ts:3-20,31-57,74-92`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\detail\model.ts:32-42,101-123`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\detail\model.py:27-45,62-105`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\detail\model.rs:114-129,166-194` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^13]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\model.cpp:34-115`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\detail\modelVariant.ts:89-188`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\detail\model_variant.py:102-172`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\IModel.cs:21-84`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\foundryLocalManager.ts:99-229`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\foundry_local_manager.py:79-196`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\FoundryLocalManager.cs:137-225,355-427`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\foundry_local_manager.rs:138-225` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^14]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\openai_chat_client.cpp:41-138`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\src\openai_audio_client.cpp:21-60`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\openai\chatClient.ts:4-57,182-250`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\openai\chat_client.py:26-84,172-260`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\OpenAI\ChatClient.cs:39-58,129-155`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\openai\chat_client.rs:18-119,203-280`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\rust\src\openai\audio_client.rs:73-197` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^15]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\foundryLocalManager.ts:215-229`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\openai\responsesClient.ts:17-18,91-118,149-305` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^16]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cs\src\FoundryLocalManager.cs:110-116`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\web-server-example\app.js:56-73`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\python\web-server\src\app.py:42-67`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\rust\foundry-local-webserver\src\main.rs:80-118` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^17]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\configuration.ts:37-48,91-97`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\js\src\detail\modelLoadManager.ts:10-18,27-40,48-81`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\configuration.py:35-37,41-65,116-123,152-163`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\python\src\detail\model_load_manager.py:22-33,49-66,91-167`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\sdk\cpp\include\configuration.h:14-23,50-55` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^18]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\web-server-example\app.js:10-18,51-73`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\python\web-server\src\app.py:40-67`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\rust\foundry-local-webserver\src\main.rs:69-118` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^19]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\langchain-integration-example\app.js:49-97` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
[^20]: `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\copilot-sdk-foundry-local\src\app.ts:20-28,83-127`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-local\repo\samples\js\copilot-sdk-foundry-local\src\tool-calling.ts:15-25,162-221` (snapshot `2d2f4dce0c0bbc0774a5b0f9284bafc04e483954`).
