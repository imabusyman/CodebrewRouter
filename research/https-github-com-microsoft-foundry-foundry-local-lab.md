# Foundry Local Lab — Deep Research Report

**Repository:** [microsoft-foundry/Foundry-Local-Lab](https://github.com/microsoft-foundry/Foundry-Local-Lab)  
**Snapshot analyzed:** `2342ef4ea5415a573926c0d23a46c9bcb1063b70`  
**Research basis:** local source inspection of the mirrored repository under `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo`.[^1]

## Executive Summary

`Foundry-Local-Lab` is best understood as a **teaching repo for building local-first AI applications on top of Foundry Local**, not as the Foundry Local runtime itself. The repository is organized as a 13-part workshop, with parallel Python, JavaScript, and C# examples that all revolve around the same lifecycle: start the service, resolve a model, download if needed, load it, then talk to the local OpenAI-compatible endpoint or a language-specific convenience client.[^1][^2]

The most important architectural pattern is that the repo separates the **control plane** (Foundry Local SDK model/service management) from the **data plane** (OpenAI-compatible completions, `ChatClient`, or `AudioClient`). That pattern is repeated in simple chat, RAG, agents, evaluation, Whisper, tool calling, and the Zava capstone app, which means the repo is really a catalog of application patterns more than a set of unrelated demos.[^2][^4][^6][^9][^10][^12]

The capstone app, **Zava Creative Writer**, is the repo's clearest production-style example: a four-stage pipeline (`Researcher -> Product Search -> Writer -> Editor`) with JSON hand-offs, streaming article output, and a bounded feedback loop. A notable nuance is that the implementation is still completely local and mostly synthetic: the "research" stage is an LLM prompt that emits structured JSON, while product retrieval is LLM-generated queries plus simple keyword overlap against a local catalog, not real web search or embeddings.[^6][^7][^8]

The repo also shows where the cross-language parity breaks down in practice. Python sometimes uses higher-level wrappers (`FoundryLocalClient`) and sometimes drops down to raw ONNX Runtime for Whisper; JavaScript uses both the OpenAI SDK and native `ChatClient`/`AudioClient`; C# is the most opinionated about packaging, with conditional WinML support for Windows and explicit QNN/NPU validation. The changelog and known-issues files show an actively maintained workshop, but also document real caveats, especially around Whisper, SDK packaging, and JavaScript client ergonomics.[^3][^5][^10][^13][^14]

## Architecture / System Overview

At a high level, the repo teaches this stack:

```text
Foundry Local runtime
  -> Foundry Local SDK / manager APIs
      -> model download / cache / load / endpoint discovery
          -> local OpenAI-compatible API or native ChatClient / AudioClient
              -> sample apps (chat, RAG, agents, eval, whisper, tool calling)
              -> Zava capstone pipeline
                  -> optional web UI over streamed NDJSON messages
```

That stack is explicit in both the docs and the code. Part 2 frames the SDK as the preferred application integration layer over the CLI, and the basic chat samples in all three languages follow the same four-step flow before creating either an OpenAI client pointed at the local endpoint or a native Foundry client surface.[^2]

## Core Findings

### 1. The repo is a workshop curriculum with code parity, not the Foundry Local implementation

The README defines the repository as a "Foundry Local Workshop" and lays out a sequential 13-part learning path: installation, SDK usage, API access, RAG, agents, multi-agent workflows, the Zava capstone, evaluation-led development, Whisper transcription, custom models, tool calling, a web UI, and a workshop wrap-up. The project structure mirrors that curriculum directly through `python/`, `javascript/`, `csharp/`, `labs/`, and `zava-creative-writer-local/` rather than any runtime or service internals.[^1]

That distinction matters technically. The code imports `FoundryLocalManager`, `OpenAI`, `Microsoft.Agents.AI`, or `agent_framework_foundry_local`; it never implements model-serving internals itself. In other words, this repo is best read as a reference implementation layer *over* Foundry Local, not as the source of Foundry Local's engine, protocol, or model runtime.[^2][^5]

### 2. The main reusable pattern is "SDK control plane, OpenAI-compatible data plane"

Part 2 is explicit that the CLI is for exploration while the SDK is for applications, and it contrasts manual service management and port discovery with `manager.start_service()` / `manager.startWebService()` / `FoundryLocalManager.CreateAsync(...)`, `manager.endpoint` / `manager.urls[0]`, and typed model lifecycle APIs.[^2]

The basic chat samples make that concrete in all three languages. Python starts a manager, checks the cache, loads the model, and sends a streaming OpenAI request to `manager.endpoint`; JavaScript does the same against `manager.urls[0] + "/v1"`; C# repeats the flow with `FoundryLocalManager.CreateAsync`, `GetCatalogAsync`, `LoadAsync`, and an `OpenAIClient` pointed at the local `/v1` endpoint.[^2]

That same control-plane/data-plane split is reused everywhere else in the repo. RAG, evaluation, tool calling, and the Zava app all manage the local service and model via Foundry Local APIs first, then route actual inference through OpenAI-compatible messages or Foundry's higher-level convenience clients.[^4][^6][^9][^10][^12]

### 3. Cross-language parity is real, but the tracks intentionally diverge in important ways

The workshop aims for parity across Python, JavaScript, and C#, but the implementations are not simple transliterations. Python's core track uses `FoundryLocalManager` plus the OpenAI SDK for chat, RAG, and evaluation, but the agent samples switch to `agent_framework_foundry_local.FoundryLocalClient`, which bundles service/model setup behind a wrapper.[^5]

JavaScript is more mixed. Basic chat, RAG, agents, evaluation, and the Zava capstone use the OpenAI SDK against the local Foundry endpoint, but tool calling switches to the SDK's native `model.createChatClient()` and Whisper uses `model.createAudioClient()`. The lab docs call this out explicitly as a convenience path distinct from the OpenAI SDK route.[^10][^12]

C# is the most "frameworked" of the three tracks. It uses `Microsoft.Agents.AI` and `AsAIAgent()` for the agent exercises, `ChatTool.CreateFunctionTool()` for tool calling, `GetAudioClientAsync()` for Whisper, and a Windows-aware packaging story in `csharp.csproj` that swaps between `Microsoft.AI.Foundry.Local.WinML` and `Microsoft.AI.Foundry.Local` depending on platform.[^3][^5][^10][^12]

There is also some source-level drift inside the parity story. The Python single-agent and multi-agent samples call `manager.unload_model(alias)` during cleanup even though the local variable in those files is `client`, not `manager`, which reads like an uncorrected example bug rather than a deliberate abstraction. That does not change the overall teaching pattern, but it is worth knowing if you plan to run the Python Agent Framework examples verbatim.[^5][^15]

### 4. The RAG examples are deliberately minimal and local-only

The Part 4 lab and the corresponding Python, JavaScript, and C# implementations all use the same idea: a small in-memory knowledge base, simple keyword-overlap retrieval, prompt grounding with the retrieved text, and streaming generation from a local model. The docs are explicit that there is no embeddings API or vector database involved, and the code matches that claim directly through hand-authored content arrays and overlap scoring.[^1][^4]

This is useful pedagogically because it isolates the shape of a RAG pipeline without pulling in infrastructure. It is also an important limitation: the repo's RAG examples teach prompt composition and local retrieval mechanics, not scalable retrieval architecture. If you want embeddings, chunk stores, or hybrid search, this repo gives you the conceptual skeleton but not the production retrieval substrate.[^4]

### 5. The Zava capstone is the repo's most valuable architectural artifact

Part 7 describes Zava as a "production-style" app with four specialized agents, structured JSON hand-offs, streaming output, and evaluator-driven retries. The docs also spell out the exact stages and retry behavior: Researcher produces JSON, Product Search returns local matches, Writer streams an article and self-feedback separated by `---`, and Editor returns an `accept`/`revise` JSON decision with feedback that can trigger up to two retries.[^6]

The implementation confirms that architecture across all three tracks. Python centralizes Foundry bootstrap in `foundry_config.py`, exposes the pipeline as a streaming FastAPI endpoint, and yields newline-delimited JSON messages from `orchestrator.py`. JavaScript has a shared `foundryConfig.mjs`, a CLI orchestrator in `main.mjs`, and an HTTP wrapper in `server.mjs`. C# keeps the whole console pipeline in one file and also has a minimal API web variant that streams the same message types.[^6][^8]

The most important technical nuance is that Zava's "research" and "product search" are intentionally local approximations, not true external integrations. The Python researcher explicitly says it replaces "Azure AI Projects + Bing Grounding" with a purely local implementation, and its concrete behavior is just "prompt the model to emit JSON." The product agent similarly states that it replaces Azure AI Search and embeddings with local keyword matching after using the model to synthesize search queries.[^7]

That makes Zava especially relevant as a design study for local-first agent orchestration. It shows how to structure stages, contracts, retries, and UI streaming without relying on external services, but it should not be mistaken for a grounded retrieval system or a real web-research pipeline.[^6][^7][^8]

### 6. Evaluation is treated as a first-class application pattern, not an add-on

Part 8 explicitly argues for "evaluation-led development" and formalizes the same accept/revise idea used by Zava's editor into an offline test harness. The code in all three languages backs that up with the same ingredients: a golden dataset, deterministic rule checks, an LLM-as-judge prompt, side-by-side prompt variants, and an aggregate scorecard.[^9]

This is one of the most reusable ideas in the repo. The samples are not benchmarking model latency or throughput; they are operationalizing *quality control* for prompt changes. That is a strong signal that the repository wants developers to think about local models as components that still need test fixtures, regression detection, and release gates.[^1][^9]

### 7. Whisper is where the language implementations diverge most sharply

The Part 9 lab draws the difference directly: JavaScript and C# use Foundry Local's built-in `AudioClient`, while Python uses the Foundry Local SDK only to download/cache the model and then runs direct ONNX Runtime inference over the encoder and decoder, including manual tokenization and 30-second chunk handling.[^10]

The code matches that split. JavaScript builds `audioClient = model.createAudioClient()` and transcribes each file through `audioClient.transcribe(...)`; C# does the same with `GetAudioClientAsync()` and `TranscribeAudioAsync(...)`; Python opens ONNX sessions, constructs cross-attention and self-attention KV tensors, autoregressively decodes tokens, and chunks longer audio to work around Whisper's 30-second encoder window.[^10]

The repo's operational notes make this area even more interesting. The changelog says JavaScript Whisper was rewritten to use `AudioClient`, but `KNOWN-ISSUES.md` still reports a major regression on the maintainers' Windows ARM64 test device where JavaScript Whisper returns empty or binary output while Python succeeds. So the workshop currently documents both the intended abstraction (`AudioClient`) and a live caveat where the lower-level Python path appears more reliable on the validated hardware.[^10][^13][^14]

### 8. Custom-model support is positioned as a practical extension path

Part 10 shows how the workshop expects users to go beyond the curated model catalog: compile a Hugging Face model into ONNX Runtime GenAI format, generate or inspect the chat template/configuration artifacts, add the compiled model to Foundry Local's cache, and then invoke it through the same local APIs used elsewhere in the repo. The lab explicitly uses `onnxruntime-genai`'s model builder, compares it with Microsoft Olive, and recommends the builder as the simplest direct path to Foundry-compatible artifacts.[^11]

This matters because it reveals how the workshop authors think about Foundry Local's extensibility. The rest of the repo teaches application patterns against prebuilt aliases; Part 10 teaches how to move the boundary and make your own model look like a first-class local Foundry asset.[^11]

### 9. Tool calling is taught as an application-owned loop, with model selection called out explicitly

Part 11 explains the tool-calling flow in four stages: define tools as JSON Schema, let the model decide whether to request them, execute them locally in application code, and send results back for a final answer. The lab is explicit that the model never executes code directly and that the application stays in control of tool execution.[^12]

The code examples then show three different ergonomics for the same flow. Python uses the OpenAI SDK with `tools=` and `tool_choice="auto"`; JavaScript uses `model.createChatClient()` and `completeChat(messages, tools)`; C# uses `ChatTool.CreateFunctionTool(...)`, checks `FinishReason == ChatFinishReason.ToolCalls`, and sends `ToolChatMessage` results back into the conversation.[^12]

The lab also makes model support an explicit design constraint by listing which aliases support tool calling and by choosing `qwen2.5-0.5b` for the examples rather than the default `phi-3.5-mini`. That is a subtle but important architectural lesson: in this repo, capability routing is sometimes a model-selection problem before it is an application-code problem.[^12]

### 10. The UI layer standardizes on streamed NDJSON over a shared browser client

Part 12 adds a single static UI (`index.html`, `style.css`, `app.js`) that is served by all three backends, and the docs define the browser/backend contract precisely: `GET /` serves the UI, `POST /api/article` runs the pipeline, and the response is read as a stream of newline-delimited JSON messages keyed by `type` (`message`, `researcher`, `marketing`, `writer`, `partial`, `editor`, `error`).[^8]

The browser code in `app.js` implements that contract literally. It reads the response stream chunk by chunk with `ReadableStream`, splits on newline boundaries, parses each JSON object, and updates status badges, detail panes, or article text incrementally based on the message type. The JavaScript and C# web servers likewise serialize one JSON object per line while using `text/event-stream`-style response headers, so the front end is effectively consuming NDJSON over a long-lived streaming HTTP response rather than relying on browser `EventSource` framing.[^8]

That protocol design is one of the repo's most portable ideas. It keeps the UI implementation language-agnostic and lets the CLI/back-end code stay close to the console pipeline while only adding a thin streaming wrapper around it.[^8][^13]

### 11. The repo is actively maintained, and the maintenance work is itself informative

The changelog shows a concentrated burst of work in March 2026: Part 11 tool calling, the Part 12 UI, a Whisper rewrite, C# WinML/QNN packaging updates, cleanup to unload models across all samples, and repeated validation passes after the Foundry Local SDK v0.9.0 update. That history makes the repo feel current and intentionally curated rather than abandoned demo code.[^13]

Just as important, the maintenance log tells you where the authors had to fight the platform. The WinML/QNN work, RID auto-detection, JavaScript `await catalog.getModel(...)` fixes, and repeated known-issues revalidation all signal that the workshop is not hand-wavy: it is tracking real integration friction across operating systems, SDK versions, and hardware-specific execution providers.[^3][^13][^14]

## Key Repositories Summary

| Repository | Purpose | Key files |
|---|---|---|
| [microsoft-foundry/Foundry-Local-Lab](https://github.com/microsoft-foundry/Foundry-Local-Lab) | Workshop repo for local AI application patterns on Foundry Local across Python, JavaScript, and C# | `README.md`, `labs/part2-foundry-local-sdk.md`, `csharp/csharp.csproj`, `python/foundry-local-whisper.py`, `javascript/foundry-local-tool-calling.mjs`, `zava-creative-writer-local/src/*`, `labs/part12-zava-ui.md`, `KNOWN-ISSUES.md`[^1][^2][^3][^6][^10][^12][^14] |

## Confidence Assessment

**High confidence** on the main architectural conclusions:

- The repo is a **workshop and reference application set**, not the Foundry Local runtime implementation.[^1][^2]
- The dominant reusable pattern is **service/model lifecycle via the Foundry SDK followed by inference through a local OpenAI-compatible endpoint or Foundry convenience client**.[^2]
- The **Zava pipeline** is the repo's main production-style artifact and is implemented as a bounded, four-stage local agent workflow with structured hand-offs and streaming output.[^6][^7][^8]
- **Evaluation, Whisper, custom models, tool calling, and UI streaming** are all first-class topics rather than afterthoughts.[^9][^10][^11][^12]

**Medium confidence** on a few implementation-quality details:

- The Python Agent Framework cleanup issue looks like a real sample bug, but I did not execute the code in this session; the confidence comes from direct source inspection rather than runtime reproduction.[^15]
- The JavaScript Whisper regression, ChatClient streaming ergonomics, and C# packaging concerns are clearly documented by the maintainers, but they are still environment-sensitive issues tied to specific SDK/CLI versions and test hardware.[^3][^10][^14]

## Footnotes

[^1]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\README.md:5-31,34-64,68-260`.
[^2]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part2-foundry-local-sdk.md:37-49,55-170`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local.py:9-52`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local.mjs:8-57`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\BasicChat.cs:18-76`.
[^3]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\csharp.csproj:3-44`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\validate-npu-workaround.ps1:18-25,32-71,98-104`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\CHANGELOG.md:7-38,45-47`.
[^4]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\README.md:112-129`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-rag.py:16-153`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local-rag.mjs:16-155`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\RagPipeline.cs:16-160`.
[^5]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\requirements.txt:1-10`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-with-agf.py:14-44`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-multi-agent.py:19-92`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local-with-agent.mjs:4-95`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local-multi-agent.mjs:16-140`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\SingleAgent.cs:21-92`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\MultiAgent.cs:21-120`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\csharp.csproj:29-43`.
[^6]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part7-zava-creative-writer.md:11-20,24-47,147-179,183-244`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\api\foundry_config.py:11-41`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\api\orchestrator.py:64-144`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\javascript\main.mjs:31-119`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\csharp\Program.cs:14-135`.
[^7]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\api\agents\researcher\researcher.py:1-27,30-63`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\api\agents\product\product.py:1-77`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\javascript\researcher.mjs:9-57`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\javascript\product.mjs:11-69`.
[^8]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\api\agents\writer\writer.py:16-87`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\api\agents\editor\editor.py:16-54`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part12-zava-ui.md:11-48,60-103,150-215`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\ui\app.js:48-199`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\javascript\server.mjs:35-180`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\zava-creative-writer-local\src\csharp-web\Program.cs:99-210`.
[^9]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part8-evaluation-led-development.md:7-18,24-55,71-103,163-220`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-eval.py:19-253`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local-eval.mjs:16-279`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\AgentEvaluation.cs:18-257`.
[^10]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part9-whisper-voice-transcription.md:42-74,112-220`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-whisper.py:42-195`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local-whisper.mjs:49-171`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\WhisperTranscription.cs:52-219`.
[^11]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part10-custom-models.md:74-126,130-177,181-215,219-260`.
[^12]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\labs\part11-tool-calling.md:7-55,101-133,137-254`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-tool-calling.py:5-119`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\javascript\foundry-local-tool-calling.mjs:20-124`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\csharp\ToolCalling.cs:17-126`.
[^13]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\CHANGELOG.md:7-103`.
[^14]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\KNOWN-ISSUES.md:9-168`.
[^15]: Snapshot `2342ef4ea5415a573926c0d23a46c9bcb1063b70`. `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-with-agf.py:19-25,43-44`; `E:\src\CodebrewRouter\research\sources\microsoft-foundry-foundry-local-lab\repo\python\foundry-local-multi-agent.py:25-31,89-93`.
