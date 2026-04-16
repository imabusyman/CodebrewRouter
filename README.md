# Blaze.LlmGateway

An intelligent, agentic LLM routing proxy for .NET 10. Blaze.LlmGateway exposes a single OpenAI-compatible API endpoint and transparently routes requests to the best available LLM provider based on intent, availability, and configuration — without requiring any changes from the client.

---

## What It Does

Blaze.LlmGateway sits between your application and multiple LLM providers. Clients send standard OpenAI-compatible chat requests to one endpoint. The gateway classifies the request, selects the most appropriate provider, injects any available MCP tools, and streams the response back — all transparently.

The gateway currently supports nine providers: Azure AI Foundry, Azure Foundry Local, Ollama (primary, backup, and local), GitHub Copilot, GitHub Models, Google Gemini, and OpenRouter.

---

## How Routing Works

Routing is handled in two stages:

**Stage 1 — Meta-routing:** The last user message is sent to a local Ollama "router" model configured to return a provider name. This gives the gateway intent-aware, semantic routing at near-zero cost.

**Stage 2 — Keyword fallback:** If the Ollama router fails or returns an unrecognized response, a keyword strategy scans the message for provider hints (e.g. "gemini", "azure", "ollama"). If no match is found, the request falls back to Azure AI Foundry.

---

## Solution Structure

| Project | Purpose |
|---|---|
| Blaze.LlmGateway.Core | Domain types: RouteDestination enum and LlmGatewayOptions configuration. No external dependencies. |
| Blaze.LlmGateway.Infrastructure | MEAI middleware pipeline, routing strategies, MCP connection management, and provider registrations. |
| Blaze.LlmGateway.Api | Minimal API host. Wires DI via extension methods and exposes the POST /v1/chat/completions SSE streaming endpoint. |
| Blaze.LlmGateway.Web | Blazor Server frontend (Syncfusion components). Scaffolded; not yet connected to the API. |
| Blaze.LlmGateway.AppHost | .NET Aspire orchestration. Provisions the Ollama container, Foundry Local, and GitHub Models. Injects all secrets as environment variables. |
| Blaze.LlmGateway.ServiceDefaults | Shared Aspire conventions: OpenTelemetry, HTTP resilience, health checks, and service discovery. |
| Blaze.LlmGateway.Tests | xUnit unit tests with Moq. 95% coverage target. |
| Blaze.LlmGateway.Benchmarks | BenchmarkDotNet project for provider latency and routing overhead analysis. |

---

## MEAI Middleware Pipeline

The gateway uses a layered Microsoft.Extensions.AI (MEAI) middleware pipeline. Requests flow from outermost to innermost:

- **McpToolDelegatingClient** — retrieves available tools from McpConnectionManager and injects them into ChatOptions before the request continues downstream.
- **LlmRoutingChatClient** — resolves the target RouteDestination via IRoutingStrategy, then forwards the request to the appropriate keyed provider client.
- **FunctionInvokingChatClient** — registered per-provider; handles automatic MCP tool call execution and result injection.
- **Keyed provider client** — the actual model call (AzureOpenAIClient, OllamaApiClient, OpenAIClient, or Google GenAI client depending on the destination).

---

## MCP Integration

The gateway integrates with the Model Context Protocol (MCP). McpConnectionManager starts as a hosted service and connects to configured MCP servers over Stdio or HTTP transport, caching the available tools. The microsoft-learn MCP server is wired by default via the Node.js npx transport.

---

## Configuration

All provider credentials and endpoints are managed as .NET Aspire parameters set on the AppHost project. This means no secrets are stored in any project config files — they are injected at runtime as environment variables.

The full LlmGatewayOptions configuration lives under the LlmGateway section, with per-provider settings under LlmGateway:Providers:[ProviderName]. Routing options including the router model name and fallback destination are also configurable via this section.

---

## Observability

The gateway is instrumented with OpenTelemetry via the shared ServiceDefaults project, covering traces, metrics, and logs. All providers participate in distributed tracing through the MEAI pipeline.

---

## Current Status

The core pipeline (routing, streaming, MCP tool injection, and Aspire orchestration) is operational. The following areas are scaffolded but not yet complete:

- Blazor Web UI — shell exists but has no API connection or chat interface.
- Circuit breaker — no provider health tracking or automatic failover.
- Streaming failover — mid-stream failure handling is not implemented.
- Authentication — no API key or bearer token enforcement on the gateway itself.
- Rate limiting and cost/token tracking are not yet implemented.
- McpConnectionManager and McpToolDelegatingClient have known implementation gaps noted in CLAUDE.md.

---

## Repository Conventions

- All LLM interactions use Microsoft.Extensions.AI abstractions. Raw HttpClient calls to LLM APIs are not permitted.
- New middleware must inherit from DelegatingChatClient.
- Provider clients are resolved via keyed DI using the RouteDestination enum name as the key.
- C# 13/14 features are used throughout: primary constructors, collection expressions, nullable reference types, and full CancellationToken propagation.
- All DI registration logic lives in Infrastructure extension methods, not in Program.cs.

---

## Agent Guidance

For architecture decisions, pipeline changes, routing strategy work, and MCP integration, use the repo-scoped Copilot agent defined in `.github/agents/llm-gateway-architect.agent.md`. It has deep familiarity with the MEAI pipeline, all providers, and the project's conventions. Full build, test, and run commands are documented in CLAUDE.md.
