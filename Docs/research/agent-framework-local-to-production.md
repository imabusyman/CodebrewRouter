# Research: Microsoft Agent Framework — Local to Production Journey

**Source:** https://devblogs.microsoft.com/foundry/from-local-to-production-the-complete-developer-journey-for-building-composing-and-deploying-ai-agents/  
**Date researched:** 2026-04-24  
**Researcher:** Copilot agent

---

## Summary

The blog post describes the **Microsoft Agent Framework (MAF) v1.0** release for Python and .NET, and the full developer journey from local prototyping to production deployment using Azure AI Foundry. It introduces a unified agentic stack that merges the enterprise foundations of **Semantic Kernel** with the multi-agent orchestration patterns of **AutoGen**, offering a single, production-grade SDK.

Key capabilities introduced:

| Capability | Description |
|---|---|
| **Microsoft Agent Framework v1.0** | Unified .NET + Python SDK; Semantic Kernel + AutoGen combined. Single agent and multi-agent orchestration with `AgentThread`, `IKernelAgent`, etc. |
| **Foundry Toolbox** | Centralized, reusable tool registry in Azure AI Foundry. Define tools once, share them across agents without rewiring per-agent. Built-in tools: code interpreter, file search, web search, SharePoint, Fabric Data, custom MCP tools. |
| **Persistent Memory** (preview) | Hosted vector-store-backed contextual memory per agent. Swap local memory for cloud-backed durable state with minimal API changes. |
| **MCP (Model Context Protocol)** | Open standard for tool calling and integration. Agents dynamically discover and invoke external tools via MCP without bespoke adapters. |
| **A2A (Agent-to-Agent) Protocol** | Open protocol for secure cross-agent collaboration. Agents on different platforms/languages can discover and delegate to each other. .NET A2A SDK in public preview. |
| **DevUI / AG-UI** | Developer UI for real-time agent session inspection, workflow management, and debugging. |
| **Foundry Agent Service** | GA managed runtime for hosting, scaling, and securing agents. End-to-end OpenTelemetry tracing, RBAC, managed keys, network isolation. |
| **Local → Production** | Start locally with the same SDK; deploy to Foundry Agent Service without rewriting. Aspire orchestration supported for local multi-service development. |

---

## .NET vs Python Assessment

**Verdict: .NET is the right choice for this repo.**

| Factor | .NET | Python |
|---|---|---|
| This repo's language | ✅ C# / .NET 10 throughout | ❌ Would require a second service |
| Microsoft Agent Framework support | ✅ First-class; same feature parity as Python for v1.0 | ✅ Also first-class |
| MEAI (`Microsoft.Extensions.AI`) | ✅ Native; already used here | ❌ Not applicable |
| Aspire orchestration | ✅ Already in `Blaze.LlmGateway.AppHost` | ❌ Not applicable |
| MCP integration | ✅ `ModelContextProtocol` NuGet; `McpConnectionManager` exists | Possible but separate process |
| Keyed DI / pipeline patterns | ✅ Already established in `InfrastructureServiceExtensions` | N/A |
| DelegatingChatClient / tooling | ✅ Already wired; `McpToolDelegatingClient` exists | N/A |

Python would only be preferable if we needed AutoGen's multi-agent orchestration patterns before the .NET SDK has them, or to prototype quickly. Since MAF v1.0 brings full parity, and the entire repo is .NET, .NET wins.

---

## What Features Can We Add from This Blog Post?

All items below are additions to the existing Blaze.LlmGateway infrastructure. They map directly to known gaps and roadmap items.

### 1. Microsoft Agent Framework Integration (High Value)

**What it is:** Replace the current bare-MEAI `IChatClient` wiring with proper `AgentThread` + `IKernelAgent` abstractions from the Microsoft Agent Framework NuGet packages.

**Packages:**
```
Microsoft.Agents.Core
Microsoft.Agents.AI
Microsoft.Agents.AI.OpenAI
```

**Current gap in this repo:** `LlmRoutingChatClient` implements routing but there is no agent thread management, no persistent conversation state, and no structured multi-agent orchestration.

**Integration path:**
- Keep `LlmRoutingChatClient` as the MEAI pipeline entry point (it already satisfies `IChatClient`).
- Layer an `AgentThread` above the HTTP endpoint so each chat session has persistent context.
- Expose a `POST /v1/agents/{agentId}/threads` endpoint for stateful agent sessions alongside the existing stateless `/v1/chat/completions`.

---

### 2. Foundry Toolbox — Centralized Tool Registry (Medium Value)

**What it is:** Instead of each agent discovering MCP tools independently at startup, register tools centrally in Azure AI Foundry and let the `McpToolDelegatingClient` pull from the Foundry Toolbox at request time.

**Current gap:** `McpConnectionManager.StartAsync()` is a placeholder; `McpToolDelegatingClient.AppendMcpTools()` appends cached stale tools. Neither speaks to a managed Foundry Toolbox endpoint.

**Integration path:**
- Implement `McpConnectionManager` to call the Foundry Toolbox REST API on startup (when `LlmGateway:Mcp:UseFoundryToolbox = true`).
- Cache tool descriptors in `IDistributedCache` with a sliding expiry.
- Fall back to the existing stdio MCP server list when Foundry is unavailable.

---

### 3. MCP Enhancements — `HostedMcpServerTool` Pattern (Medium Value)

**What it is:** The blog highlights that MAF uses `HostedMcpServerTool` (MEAI) to map Foundry-hosted MCP tools into `ChatOptions.Tools`. This is the missing piece in `McpToolDelegatingClient`.

**Current gap (known):** `McpToolDelegatingClient.AppendMcpTools` appends raw cached tools directly rather than mapping to `HostedMcpServerTool` instances.

**Integration path:**
- Replace the raw tool-append loop in `McpToolDelegatingClient` with `HostedMcpServerTool` wrapping once the `ModelContextProtocol` SDK exposes the type.
- This removes the manual tool-dispatch logic and lets MEAI's `FunctionInvokingChatClient` handle invocation correctly.

---

### 4. A2A (Agent-to-Agent) Protocol — Multi-Agent Routing (Low-Medium Value, Future)

**What it is:** Agents can delegate sub-tasks to other agents using an open HTTP-based A2A protocol. The `CodebrewRouter` virtual destination in this repo is a natural fit: it could expose an A2A endpoint so external agents route through Blaze as a gateway.

**NuGet:** `Microsoft.Agents.Protocols.A2A` (public preview)

**Integration path:**
- Expose a `POST /.well-known/agent.json` descriptor endpoint (A2A discovery).
- Map incoming A2A task requests to the existing `/v1/chat/completions` pipeline.
- This makes Blaze.LlmGateway a first-class A2A participant — other agents can route through it for model selection.

---

### 5. Persistent Memory — Per-Session Vector Store (Medium Value)

**What it is:** Agents maintain contextual memory across sessions using Foundry's hosted vector store or a local in-memory fallback. This is tied to `AgentThread` lifecycle.

**Current gap:** No session memory exists. Each request is stateless.

**Integration path:**
- Add an `IAgentMemory` abstraction in `Blaze.LlmGateway.Core` with two implementations:
  - `InMemoryAgentMemory` (default, local dev)
  - `FoundryVectorStoreMemory` (uses Azure AI Foundry vector store API)
- Inject into `AgentThread` so the system prompt accumulates relevant context from prior turns.
- Wire via Aspire parameter `LlmGateway:Memory:Provider = InMemory | FoundryVectorStore`.

---

### 6. DevUI / Aspire Dashboard Integration (Low Value, QoL)

**What it is:** Real-time inspection of agent sessions, routing decisions, and tool calls during development.

**Current gap:** The Aspire dashboard shows basic service health. Routing decisions and tool invocations are not surfaced as structured spans.

**Integration path:**
- Add structured OpenTelemetry spans in `LlmRoutingChatClient` (`RouteDecision`, `ProviderSelected`, `FailoverActivated`).
- Add spans in `McpToolDelegatingClient` for each tool appended and each invocation.
- The Aspire dashboard's trace viewer will display these automatically.

---

### 7. Foundry Agent Service Deployment Target (Medium Value)

**What it is:** Instead of (or alongside) `dotnet run --project Blaze.LlmGateway.AppHost`, deploy to Foundry Agent Service with managed identity, RBAC, and auto-scaling.

**Current gap:** Blaze is containerized via Aspire but has no Foundry Agent Service deployment descriptor.

**Integration path:**
- Add a `Docs/deploy/foundry-agent-service.md` deployment guide.
- Add a `bicep/` or `azd/` deployment template (using `azd up`) to the `AppHost`.
- The existing managed identity support in `Blaze.LlmGateway.ServiceDefaults` already handles `AzureCliCredential` chaining.

---

## Priority Order for Implementation

| # | Feature | Effort | Value |
|---|---|---|---|
| 1 | **MCP fix: `HostedMcpServerTool` mapping** | Small | High — fixes a known gap |
| 2 | **OpenTelemetry routing spans for DevUI** | Small | High — observability |
| 3 | **Agent Framework: `AgentThread` stateful sessions** | Medium | High — core differentiator |
| 4 | **Foundry Toolbox integration** | Medium | High — tool management |
| 5 | **Persistent Memory (`IAgentMemory`)** | Medium | Medium |
| 6 | **A2A protocol endpoint** | Large | Medium — future differentiator |
| 7 | **Foundry Agent Service deployment** | Medium | Medium — production path |

---

## NuGet Package Checklist

```xml
<!-- Microsoft Agent Framework v1.0 -->
<PackageReference Include="Microsoft.Agents.Core" Version="1.*" />
<PackageReference Include="Microsoft.Agents.AI" Version="1.*" />
<PackageReference Include="Microsoft.Agents.AI.OpenAI" Version="1.*" />

<!-- A2A protocol (preview) -->
<PackageReference Include="Microsoft.Agents.Protocols.A2A" Version="0.*" />
```

> **Security note:** Run `gh-advisory-database` check before adding these packages. They are from the `microsoft` org but are still in rapid iteration.

---

## Related Files in This Repo

| File | Relevance |
|---|---|
| `Blaze.LlmGateway.Infrastructure/McpConnectionManager.cs` | Fix `StartAsync` placeholder; add Foundry Toolbox source |
| `Blaze.LlmGateway.Infrastructure/McpToolDelegatingClient.cs` | Replace raw tool-append with `HostedMcpServerTool` |
| `Blaze.LlmGateway.Infrastructure/LlmRoutingChatClient.cs` | Add OTel routing spans; wire `AgentThread` |
| `Blaze.LlmGateway.Core/RouteDestination.cs` | May need `A2AAgent` destination if A2A is added |
| `Blaze.LlmGateway.AppHost/Program.cs` | Add Foundry Agent Service resource or `azd` target |
| `Blaze.LlmGateway.Api/ProgramPartial.cs` | Add `/v1/agents/{id}/threads` endpoint |

---

## References

- Blog post: https://devblogs.microsoft.com/foundry/from-local-to-production-the-complete-developer-journey-for-building-composing-and-deploying-ai-agents/
- Microsoft Agent Framework GitHub: https://github.com/microsoft/agent-framework
- A2A .NET SDK blog: https://devblogs.microsoft.com/foundry/building-ai-agents-a2a-dotnet-sdk/
- Foundry Agent Service C# walkthrough: https://medium.com/microsoftazure/build-production-ready-ai-agents-in-csharp-with-azure-ai-foundry-882e3e7e39b7
- N+1 Blog — MAF + Azure AI Foundry: https://nikiforovall.blog/dotnet/ai/2026/03/24/microsoft-agent-framework-azure-ai-foundry.html
- Related ADR in this repo: `Docs/design/adr/0006-azure-foundry-agents-integration.md`
