# Blaze.LlmGateway - Copilot Architecture Rules

You are assisting a Lead C# Architect on a .NET 10 LLM Gateway project. Strictly adhere to the following rules:

## 1. Microsoft.Extensions.AI (MEAI) is the Law
- NEVER use raw `HttpClient` to call LLM APIs.
- ALWAYS use the `IChatClient` interface from `Microsoft.Extensions.AI` for all LLM interactions.
- ALWAYS use `ChatMessage`, `ChatOptions`, and `ChatRole` from the `Microsoft.Extensions.AI` namespace.
- If adding new middleware features (e.g., caching, logging), implement a class inheriting from `DelegatingChatClient`.

## 2. Provider Mappings (Crucial)
When adding or modifying provider connections in `Program.cs`, adhere to these SDK mappings:
- **Azure Foundry (Azure AI):** Use `AzureOpenAIClient` -> `.AsChatClient()`.
- **Ollama:** Use `OllamaApiClient` -> `.AsChatClient()`.
- **GitHub Models:** Use standard `OpenAIClient` with the endpoint set to GitHub's inference URL -> `.AsChatClient()`.
- **OpenRouter:** Use standard `OpenAIClient` with the endpoint set to `https://openrouter.ai/api/v1` -> `.AsChatClient()`.
- **Gemini:** Use `Google.Generative.AI.GenerativeAIClient` -> `.AsIChatClient()`. 

## 3. Keyed Dependency Injection
- We manage multiple `IChatClient` implementations via Keyed DI. 
- When resolving a specific provider inside the router middleware, use `IServiceProvider.GetKeyedService<IChatClient>("ProviderName")`.

## 4. Model Context Protocol (MCP) Standards
- ALWAYS use the official `ModelContextProtocol` NuGet package for connecting to external tools.
- NEVER write custom tool-calling loops. Rely entirely on MEAI's `FunctionInvokingChatClient` (via `.UseFunctionInvocation()`) to handle the execution of tool calls and re-prompting.
- To expose an MCP tool to an MEAI client, map the MCP tool definition to a `HostedMcpServerTool` (from `Microsoft.Extensions.AI.Abstractions`) and append it to `ChatOptions.Tools`.

## 5. Streaming by Default
- The `POST /v1/chat/completions` endpoint acts as a proxy for external IDE tools. It MUST support Server-Sent Events (SSE) streaming.
- Use `CompleteStreamingAsync` and `IAsyncEnumerable<T>` extensively. 

## 6. Modern .NET 10 Practices
- Use primary constructors.
- Use collection expressions (`[]`).
- Keep `Program.cs` clean; extract complex DI setup into extension methods.