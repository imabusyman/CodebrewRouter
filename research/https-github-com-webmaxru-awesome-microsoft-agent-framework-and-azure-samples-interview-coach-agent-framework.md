# Research report: [webmaxru/awesome-microsoft-agent-framework](https://github.com/webmaxru/awesome-microsoft-agent-framework) and [Azure-Samples/interview-coach-agent-framework](https://github.com/Azure-Samples/interview-coach-agent-framework)

**Snapshot analyzed:** `webmaxru/awesome-microsoft-agent-framework` at `b945892ddbda1185bd4e12e6b4210971b0859de5`; `Azure-Samples/interview-coach-agent-framework` at `a279916bbef37fc1dcbf09f08709e580e7a7f562`.[^1][^2]

**Research basis:** mirrored local source inspection under `research\sources\awesome-microsoft-agent-framework\repo` and `research\sources\interview-coach-agent-framework\repo`.[^1][^2]

## Executive Summary

[webmaxru/awesome-microsoft-agent-framework](https://github.com/webmaxru/awesome-microsoft-agent-framework) is a curated discovery layer for Microsoft Agent Framework rather than an implementation repo: its public value is a categorized README of docs, videos, blog posts, tutorials, samples, tools, and related technologies, backed by lightweight contribution rules and a single `awesome-lint` npm test script.[^3][^4][^5]

[Azure-Samples/interview-coach-agent-framework](https://github.com/Azure-Samples/interview-coach-agent-framework) is the opposite: a runnable .NET 10 reference application that combines Aspire orchestration, a Blazor AG-UI frontend, an Agent Framework-powered agent service, two MCP-backed tool integrations, SQLite persistence, and provider-swappable OpenAI-compatible model wiring.[^2][^6][^7][^8][^9][^10]

The strongest architectural idea in the Interview Coach sample is its separation of concerns: AppHost owns topology and provider wiring, the agent service owns orchestration and protocol surfaces, MCP servers own tool/data access, and the UI consumes the agent through AG-UI instead of talking to providers or databases directly.[^6][^7][^8][^9][^11]

The most important caveat is that parts of the sample are still transitional. The documentation describes a `CopilotHandOff` mode, but the runtime factory only enables `Single` and `LlmHandOff`, with the GitHub Copilot workflow left commented out. The sample also uses ephemeral in-memory file upload storage and ships a narrow automated test surface centered on a specific upstream handoff-tool workaround rather than end-to-end orchestration tests.[^12][^13][^14]

## Architecture / system overview

The two repositories play different roles in the same ecosystem.[^3][^6]

```text
┌─────────────────────────────────────────────────────────────────────┐
│ webmaxru/awesome-microsoft-agent-framework                         │
│  - Curated map of docs, videos, tutorials, samples, tools         │
│  - Lightweight maintenance via README + awesome-lint              │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Azure-Samples/interview-coach-agent-framework                      │
│  - Runnable end-to-end sample                                      │
│  - Demonstrates how to combine MAF + MCP + Aspire + AG-UI         │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌───────────┐   AG-UI    ┌──────────────┐   MCP    ┌─────────────────┐
│  Web UI   │──────────▶│ Agent service │────────▶│ MarkItDown MCP   │
│ (Blazor)  │           │ (MAF/OpenAI)  │         │ + InterviewData  │
└───────────┘           └──────┬───────┘         └────────┬────────┘
                                │                          │
                                ▼                          ▼
                         ┌──────────────┐           ┌──────────────┐
                         │ LLM provider │           │   SQLite     │
                         │ (Foundry /   │           │ session data │
                         │ AOAI / GH)   │           └──────────────┘
                         └──────────────┘
```

## Key repositories summary

| Repository | What it is | What it is useful for | Key evidence |
| --- | --- | --- | --- |
| [webmaxru/awesome-microsoft-agent-framework](https://github.com/webmaxru/awesome-microsoft-agent-framework) | Curated ecosystem index for Microsoft Agent Framework resources | Discovering official docs, migration guides, samples, tools, videos, and community content | `README.md`, `contributing.md`, `package.json`[^3][^4][^5] |
| [Azure-Samples/interview-coach-agent-framework](https://github.com/Azure-Samples/interview-coach-agent-framework) | End-to-end sample app using Agent Framework, MCP, Aspire, and AG-UI | Learning runtime composition, multi-agent handoff design, provider abstraction, MCP integration, and Azure deployment | `README.md`, `docs\ARCHITECTURE.md`, `src\InterviewCoach.*`, `azure.yaml`[^2][^6][^7][^8][^9][^10][^24] |

## Repository 1: `awesome-microsoft-agent-framework`

### What the repo actually contains

This repository is a curated “awesome list,” not an SDK or sample implementation. Its README is the product: the table of contents and body are organized into Getting Started, Official Documentation, Video Resources, Blog Posts & Articles, Tutorials, Examples & Samples, Tools & Frameworks, Related Technologies, and Community sections.[^3]

That structure makes it useful as a discovery map for the broader Agent Framework ecosystem, especially for onboarding and landscape scans. The README links official installation guides, migration guides, Microsoft-authored blogs, official sample folders, and community examples in one place, so it is more of a navigational asset than a technical artifact.[^3]

### Maintenance model

The repo’s maintenance workflow is intentionally light. Contributors are instructed to add entries directly to `README.md` using a uniform `[Resource Name](URL) - Description.` format, submit one suggestion per pull request, keep descriptions concise, and improve categorization when needed.[^5]

The only checked-in automation surfaced in the package manifest is an npm `test` script that runs `awesome-lint`, which reinforces that repository quality is primarily about curation and formatting rather than executable behavior.[^4]

### Practical assessment

For research or implementation work, this repo should be treated as a launchpad rather than a source of truth about runtime behavior. It is valuable for finding the official [microsoft/agent-framework](https://github.com/microsoft/agent-framework) repo, documentation, samples, DevUI, Agent Skills, and community write-ups, but it does not itself explain how to compose services, wire protocols, or deploy an application.[^3][^4][^5]

One practical nuance: in the mirrored snapshot analyzed here, the Examples & Samples section points to official Agent Framework sample folders and two community projects, but it does not include the Interview Coach sample reviewed below.[^3]

## Repository 2: `interview-coach-agent-framework`

### What the sample is trying to teach

The sample positions itself as an AI-powered interview coach that demonstrates how to combine Microsoft Agent Framework, MCP, and Aspire in a deployable application. The README explicitly frames the sample around multi-agent orchestration, MCP-based tool addition, cross-session state, LLM provider swapping, and `azd up` deployment.[^2]

The project is pinned to .NET 10 (`10.0.100`) and its main service projects target `net10.0`, which is consistent with its role as a current-generation platform sample rather than a backward-compatible reference implementation.[^10]

### High-level topology

The documentation and AppHost code agree on the same service graph:

1. Aspire orchestrates the full application graph.
2. `InterviewCoach.WebUI` is the Blazor frontend.
3. `InterviewCoach.Agent` is the agent runtime and protocol host.
4. `InterviewCoach.Mcp.InterviewData` is a custom MCP server for persistence.
5. MarkItDown runs as an external MCP container for document parsing.
6. SQLite backs the custom MCP server’s interview-session store.[^6][^7]

`InterviewCoach.AppHost\AppHost.cs` materializes that topology directly by adding the MarkItDown container, a SQLite resource, the InterviewData project, the Agent project, and the WebUI project, then wiring service references and startup dependencies between them.[^7]

### Provider abstraction lives in AppHost, not in the agent

One of the sample’s most interesting design choices is that the LLM provider abstraction is mostly owned by AppHost. `LlmResourceFactory.WithLlmReference(...)` parses provider and mode, validates combinations, and attaches one of four provider configurations: GitHub Models, Azure OpenAI, Microsoft Foundry, or GitHub Copilot.[^11]

The provider guide and default `apphost.settings.json` reinforce that switching providers is meant to be a configuration concern, not an agent-code change: the same app can run against Foundry, Azure OpenAI, or GitHub Models by changing config or CLI arguments.[^15][^25]

Inside `InterviewCoach.Agent\Program.cs`, the agent service simply asks Aspire for an OpenAI chat client named `"chat"`; even the `MicrosoftFoundry` branch currently resolves through the same `AddOpenAIClient("chat").AddChatClient()` path, while older Foundry-specific code is commented out. In practice, that means AppHost normalizes provider-specific wiring into a common OpenAI-compatible resource boundary before the agent consumes it.[^8][^11]

### Agent service: three protocol surfaces plus file-upload glue

`InterviewCoach.Agent\Program.cs` does four major things:

1. Creates two MCP HTTP clients, one for MarkItDown and one for InterviewData.
2. Registers the AI agent through `AddAIAgent("coach")`.
3. Exposes OpenAI Responses and OpenAI Conversations endpoints.
4. Exposes an AG-UI endpoint at `ag-ui` and a development-only DevUI surface.[^8]

This is significant because the service is not just “an agent.” It is also a protocol adapter layer: it can talk to a UI through AG-UI, expose OpenAI-compatible surfaces, and call external tool servers over MCP.[^8]

The same service also hosts upload endpoints. Uploaded files are stored in a process-local `ConcurrentDictionary`, validated by extension and size, assigned a generated URL, and then served back through `/uploads/{fileId}/{fileName}`. That makes attachments easy for the UI and MarkItDown to consume, but it also means uploaded content is ephemeral and tied to the lifetime of the agent process.[^8][^13]

### Web UI: AG-UI client plus local session framing

The WebUI is a Blazor Server app that uses `AGUIChatClient` pointed at the agent’s `ag-ui` endpoint; it does not know about providers, MCP servers, or the database directly.[^9]

Within `Chat.razor`, the UI creates a new GUID-backed session, sends that session ID as a system message, mirrors it into `ChatOptions.ConversationId`, and keeps a `statefulMessageCount` marker so subsequent outbound messages preserve the right conversation framing. The same component handles streaming updates, appends non-text content to the message list, and turns file attachments into upload URLs before sending the user message onward.[^20]

This creates a two-layer state model:

1. **UI/AG-UI conversation state** via `ConversationId` and injected system messages.[^20]
2. **Durable interview-session state** in the custom MCP server’s SQLite store.[^19]

That separation is useful. The UI owns chat continuity, while the InterviewData MCP server owns interview artifacts such as resume text, job description text, transcript, and completion status.[^19][^20]

### MCP integration: one external tool server, one internal tool server

The sample uses two different MCP patterns side by side:

1. **External reusable server:** MarkItDown runs as a containerized MCP server and is consumed over HTTP/SSE from the agent service.[^6][^7][^8]
2. **Internal app-specific server:** `InterviewCoach.Mcp.InterviewData` is a custom .NET MCP server built with `ModelContextProtocol.AspNetCore`, mapped at `/mcp`, and configured with stateless HTTP transport plus tool discovery from the entry assembly.[^16][^19]

This pairing is arguably the sample’s clearest teaching moment. It demonstrates that MCP is not only for third-party integrations; it is also a clean way to isolate internal data or business capabilities behind a tool boundary.[^6][^16][^19]

### Persistence model and transcript semantics

The custom InterviewData server models each interview session as a single EF Core entity with fields for resume link/text, job-description link/text, transcript, completion flag, and timestamps.[^16]

The `InterviewSessionTool` exposes add/get/update/complete operations as MCP tools using `[McpServerTool]` attributes, while `InterviewSessionRepository` implements them on top of EF Core.[^19]

One subtle but important behavior lives in `UpdateInterviewSessionAsync(...)`: transcript updates are appended, not replaced. The repository builds a new transcript by concatenating the stored transcript and the incoming transcript fragment before issuing an `ExecuteUpdateAsync(...)`. That means agents can progressively build the interview log over time rather than overwriting it on each turn.[^19]

### Multi-agent design: sequential handoff, not a generic router mesh

The sample’s multi-agent mode is more opinionated than the high-level docs might suggest. The agent factory defines five specialists — `triage`, `receptionist`, `behavioural_interviewer`, `technical_interviewer`, and `summariser` — and the comments explain that the topology was changed from a pure hub-and-spoke pattern to a sequential chain with Triage as fallback.[^12][^17]

That change matters. The code comments say the previous approach allowed the stateless triage agent to re-route users back to already-completed phases based on keywords like “resume” or “job description.” The current workflow instead drives the happy path directly from Receptionist → Behavioural → Technical → Summariser, while still allowing specialists to hand back to Triage for unexpected requests.[^12]

The sample therefore teaches a specific orchestration lesson: when agents represent ordered phases of a business process, direct phase-to-phase handoffs can be more reliable than always bouncing through a general router.[^12][^17]

### Single-agent mode vs. handoff mode

`AgentDelegateFactory.AddAIAgent(...)` currently enables two modes:

1. `Single`, which builds one `ChatClientAgent` with all tools.
2. `LlmHandOff`, which builds the five-agent workflow and wraps it as an `AIAgent`.[^26]

In `Single` mode, one prompt handles session setup, document intake, behavioral questions, technical questions, and summarization with access to both MarkItDown and InterviewData tools.[^12]

In `LlmHandOff` mode, tools are intentionally scoped: Triage has no tools; Receptionist gets MarkItDown plus InterviewData; the interviewers and summariser only get InterviewData. That is a concrete least-privilege pattern, not just a conceptual guideline.[^12][^17]

### Documented Copilot mode vs. implemented Copilot mode

A particularly important gap is the mismatch between docs and code around GitHub Copilot:

- The documentation presents `CopilotHandOff` as a first-class third mode and explains how it should work.[^17]
- `LlmResourceFactory` also includes a `GitHubCopilot` provider path that injects a token into the environment.[^11]
- But `AgentDelegateFactory.AddAIAgent(...)` has the `CopilotHandOff` case commented out, and the entire `CreateCopilotHandOffWorkflow(...)` implementation is commented out as well.[^12]

So the repo is best understood as **Copilot-aware but not Copilot-complete** at this snapshot. The documentation describes the intended shape, but the executable agent factory only activates the OpenAI-compatible and handoff-with-LLM paths.[^11][^12][^17]

### Compatibility shims and upstream workarounds

Two pieces of custom glue stand out because they patch framework edge cases rather than domain logic:

1. `WorkflowExtensions.SetName(...)` uses reflection to set a workflow `Name` property/backing field, which suggests the desired naming API is not directly exposed in the way the sample needs.[^22]
2. `HandoffToolResultFix` wraps a workflow agent with streaming middleware that converts plain string `FunctionResultContent.Result` values into `JsonElement` instances before AG-UI serialization. The file explicitly references upstream Agent Framework issues and explains that string tool results were causing AGUI JSON deserialization failures.[^21]

The included xUnit tests are entirely focused on this workaround, validating conversion of string tool results, pass-through of JSON and null results, and stream behavior. That makes the visible automated test investment precise and valuable, but also narrow: the test surface shown in the repo is concentrated on one interoperability fix, not the full application flow.[^14][^21]

### Operational posture and deployment

The sample is clearly designed to be deployable, not just runnable locally. The README documents `azd up` / `azd down`, and `azure.yaml` declares the AppHost project as a `containerapp` service for Azure deployment.[^2][^24]

At the same time, the project documentation is candid about production trade-offs. The FAQ says the overall architecture patterns are solid for production use, but specifically warns that SQLite is a temporary storage choice under load and calls out the need for stronger security, monitoring, and error handling decisions before real deployment.[^23]

The checked-in GitHub workflow visible in the mirrored snapshot is a GitHub Pages deployment that uploads the `samples/` folder, which suggests the repo’s current public automation emphasis is documentation/sample-asset publishing rather than a broad CI matrix.[^14]

## How the two repositories fit together

Taken together, the two repositories occupy complementary layers of the same ecosystem:

1. **Discovery layer:** the awesome list helps engineers find the official framework repo, docs, tutorials, samples, tools, and adjacent technologies.[^3]
2. **Implementation layer:** the Interview Coach sample shows one opinionated way to operationalize those concepts in a real .NET application using AG-UI, MCP, Aspire, OpenAI-compatible hosting, and Azure deployment.[^2][^6][^7][^8][^9][^24]

If I were using these repos together, I would treat the awesome list as an ecosystem map and the Interview Coach repo as a pattern catalog for:

- multi-service local orchestration with Aspire,[^7][^18]
- tool isolation with MCP servers,[^8][^16][^19]
- provider abstraction through AppHost,[^11][^15][^25]
- AG-UI-based frontend/backend decoupling,[^8][^9][^20]
- and phase-based multi-agent handoff design.[^12][^17]

## Recommended takeaways

### When `awesome-microsoft-agent-framework` is the right source

Use the awesome list when you need breadth: official documentation, migration material, related tools, sample links, and community learning resources in one place.[^3]

### When `interview-coach-agent-framework` is the right source

Use the Interview Coach sample when you need depth on a concrete .NET application architecture for Agent Framework. It is especially useful if you want an existence proof for AG-UI hosting, MCP tool composition, multi-provider wiring, and Aspire-managed service boundaries.[^2][^6][^7][^8][^9][^11]

### What I would copy vs. what I would treat as sample-only

I would copy these ideas into real work:

1. AppHost-owned provider abstraction.[^11]
2. MCP isolation for reusable or separately owned capabilities.[^16][^19]
3. Sequential handoff for strictly ordered workflows.[^12][^17]
4. Shared service defaults for resilience, health checks, and OpenTelemetry.[^18]

I would treat these as sample-specific or needing hardening:

1. In-memory upload storage in the agent service.[^8][^13]
2. SQLite as long-term persistence.[^16][^23]
3. The documented-but-disabled Copilot handoff path until the code is completed.[^11][^12][^17]
4. Reflection-based workflow naming and temporary AG-UI/handoff result shims, which appear to be compensating for current framework gaps.[^21][^22]

## Confidence Assessment

**High confidence:** the overall characterization of the awesome list as a curated index, the Interview Coach sample’s service topology, the AG-UI/MCP/Aspire composition, the provider abstraction layer in AppHost, the sequential handoff topology, the Copilot-mode implementation gap, and the persistence/upload behaviors are all directly visible in mirrored source files and docs.[^3][^6][^7][^8][^9][^11][^12][^16][^19][^20][^21]

**Moderate confidence:** the sample’s intended production posture is partly inferred from documentation tone plus deployment assets rather than a full CI/CD or ops implementation, so I am confident it is *designed as a deployable reference app* but not claiming it is production-complete.[^2][^14][^23][^24]

**Low-confidence / avoided claims:** I did not infer unverified performance characteristics, deep runtime behavior inside external dependencies, or any undocumented reasons behind the disabled Copilot workflow beyond what the commented code and docs themselves state.[^11][^12][^17]

## Footnotes

[^1]: `research\sources\awesome-microsoft-agent-framework\repo\README.md:1-33` and `research\sources\awesome-microsoft-agent-framework\repo\package.json:1-9` (commit `b945892ddbda1185bd4e12e6b4210971b0859de5`)
[^2]: `research\sources\interview-coach-agent-framework\repo\README.md:1-68` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^3]: `research\sources\awesome-microsoft-agent-framework\repo\README.md:1-166` (commit `b945892ddbda1185bd4e12e6b4210971b0859de5`)
[^4]: `research\sources\awesome-microsoft-agent-framework\repo\package.json:1-9` (commit `b945892ddbda1185bd4e12e6b4210971b0859de5`)
[^5]: `research\sources\awesome-microsoft-agent-framework\repo\contributing.md:1-23` (commit `b945892ddbda1185bd4e12e6b4210971b0859de5`)
[^6]: `research\sources\interview-coach-agent-framework\repo\docs\ARCHITECTURE.md:5-129` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^7]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.AppHost\AppHost.cs:1-32` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^8]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\Program.cs:13-176` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^9]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.WebUI\Program.cs:7-44` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^10]: `research\sources\interview-coach-agent-framework\repo\global.json:1-6`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\InterviewCoach.Agent.csproj:1-34`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.WebUI\InterviewCoach.WebUI.csproj:1-18`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Mcp.InterviewData\InterviewCoach.Mcp.InterviewData.csproj:1-20` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^11]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.AppHost\LlmResourceFactory.cs:20-191`; `research\sources\interview-coach-agent-framework\repo\docs\providers\README.md:1-129`; `research\sources\interview-coach-agent-framework\repo\apphost.settings.json:11-38` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^12]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\AgentDelegateFactory.cs:27-291` and `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\AgentDelegateFactory.cs:294-456` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^13]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\Program.cs:131-174`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.WebUI\Services\FileUploadService.cs:1-46`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.WebUI\Components\Pages\Chat\Chat.razor:58-80` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^14]: `research\sources\interview-coach-agent-framework\repo\.github\workflows\static.yml:1-43`; `research\sources\interview-coach-agent-framework\repo\tests\InterviewCoach.Agent.Tests\HandoffToolResultFixTests.cs:10-203` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^15]: `research\sources\interview-coach-agent-framework\repo\docs\providers\README.md:23-124`; `research\sources\interview-coach-agent-framework\repo\apphost.settings.json:11-38` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^16]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Mcp.InterviewData\Program.cs:18-41`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Mcp.InterviewData\InterviewDataDbContext.cs:5-39` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^17]: `research\sources\interview-coach-agent-framework\repo\docs\MULTI-AGENT.md:1-212` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^18]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.ServiceDefaults\Extensions.cs:21-127` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^19]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Mcp.InterviewData\InterviewSessionTool.cs:16-99`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Mcp.InterviewData\InterviewSessionRepository.cs:16-93`; `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Mcp.InterviewData\InterviewDataDbContext.cs:5-39` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^20]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.WebUI\Components\Pages\Chat\Chat.razor:24-181` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^21]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\HandoffToolResultFix.cs:1-71`; `research\sources\interview-coach-agent-framework\repo\tests\InterviewCoach.Agent.Tests\HandoffToolResultFixTests.cs:10-203` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^22]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\WorkflowExtensions.cs:5-33` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^23]: `research\sources\interview-coach-agent-framework\repo\docs\FAQ.md:23-39`; `research\sources\interview-coach-agent-framework\repo\docs\FAQ.md:99-137`; `research\sources\interview-coach-agent-framework\repo\docs\FAQ.md:149-200` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^24]: `research\sources\interview-coach-agent-framework\repo\azure.yaml:1-14`; `research\sources\interview-coach-agent-framework\repo\README.md:54-67` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^25]: `research\sources\interview-coach-agent-framework\repo\apphost.settings.json:11-38` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
[^26]: `research\sources\interview-coach-agent-framework\repo\src\InterviewCoach.Agent\AgentDelegateFactory.cs:29-64` (commit `a279916bbef37fc1dcbf09f08709e580e7a7f562`)
