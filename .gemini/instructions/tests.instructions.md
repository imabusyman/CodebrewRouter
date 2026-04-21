---
applyTo: "Blaze.LlmGateway.Tests/**, Blaze.LlmGateway.Benchmarks/**"
---

# Test conventions

Apply to: every file under `Blaze.LlmGateway.Tests/` and `Blaze.LlmGateway.Benchmarks/`.

## Framework

- xUnit (v2.9.3) + Moq (v4.20.72).
- Global `using Xunit;` is implicit in the test project.
- BenchmarkDotNet for `Blaze.LlmGateway.Benchmarks/`.

## Naming

- Class: `<ClassUnderTest>Tests.cs` under `Blaze.LlmGateway.Tests/<Area>/`.
- Method: behavioral sentence — `Returns<Outcome>_When<Condition>`, `Routes<Target>_When<Condition>`, `Throws<ExceptionName>_When<Condition>`.
- `[Fact]` for single-case; `[Theory]` with `[InlineData]` for parameterized.

## Targeted runs

```powershell
dotnet test --no-build --filter "FullyQualifiedName~<YourNewTestClass>"
```

Once targeted passes:

```powershell
dotnet build --no-incremental -warnaserror
dotnet test --no-build --collect:"XPlat Code Coverage"
```

## Mocking `IChatClient`

```csharp
var chatClient = new Mock<IChatClient>();

chatClient
    .Setup(c => c.GetResponseAsync(
        It.IsAny<IEnumerable<ChatMessage>>(),
        It.IsAny<ChatOptions>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "...")]));

chatClient
    .Setup(c => c.GetStreamingResponseAsync(
        It.IsAny<IEnumerable<ChatMessage>>(),
        It.IsAny<ChatOptions>(),
        It.IsAny<CancellationToken>()))
    .Returns(AsyncEnumerable());

static async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
{
    yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk 1");
    yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk 2");
    await Task.CompletedTask;
}
```

Never mock `HttpClient` directly for LLM calls — that implies raw HTTP, which is banned by MEAI law.

## Keyed DI in tests

```csharp
var services = new ServiceCollection();
services.AddKeyedSingleton<IChatClient>("Ollama", chatClient.Object);
services.AddKeyedSingleton<IChatClient>("AzureFoundry", fallback.Object);
var sp = services.BuildServiceProvider();
var sut = new LlmRoutingChatClient(sp, strategy.Object);
```

## SSE integration tests

```csharp
using var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(b => b.ConfigureServices(svc => { /* swap IChatClient */ }));
using var client = factory.CreateClient();
using var response = await client.PostAsync("/v1/chat/completions",
    new StringContent("""{"messages":[{"role":"user","content":"hi"}]}""",
        Encoding.UTF8, "application/json"));

response.EnsureSuccessStatusCode();
Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

var body = await response.Content.ReadAsStringAsync();
Assert.Contains("data:", body);
Assert.EndsWith("data: [DONE]\n\n", body);
```

Every streaming test asserts:
1. `Content-Type: text/event-stream`
2. At least one `data:` chunk.
3. Final `data: [DONE]\n\n` terminator.
4. Chunk JSON shape: `{"choices":[{"delta":{"content":"..."}}]}`.

## Coverage

95% line coverage on the production files listed in the envelope. Compute from `TestResults/<guid>/coverage.cobertura.xml`.

## Prove-It

Every test must actually fail against the pre-fix baseline. If a new test passes against both the old code and the new code, it's insufficient — tighten the assertions.
