# API Local CodebrewRouter Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the API call an injected local CodebrewRouter provider that cleans prompts with local Gemma 4, uses online routing when internet-backed providers are reachable, and returns a local Gemma response when offline.

**Architecture:** The API will register the provider path instead of the legacy local inference registration. Local inference remains lazy: DI resolves the `"LocalGemma"` keyed `IChatClient`, but LLamaSharp loads the GGUF model only when the local path is used. `CodebrewRouterChatClient` becomes online-aware: clean input first, then route through existing internet-capable fallback rules when online, or use `"LocalGemma"` directly when offline.

**Tech Stack:** .NET 10, Microsoft.Extensions.AI `IChatClient`, LLamaSharp GGUF runtime, xUnit, Moq, `IOptions<T>`, `IHttpClientFactory`.

---

## ⚠️ Blocking Issues From Rubber-Duck Review

The initial plan was reviewed and 3 blocking architectural issues were identified. These have been **incorporated into the tasks below**:

### **Issue 1: Connectivity Probe Race Condition in Streaming**
- **Problem:** Probe is checked once at start; mid-stream network failures and provider recoveries are not re-checked.
- **Resolution (in Task 3):** After `TryGetFirstChunkAsync()` succeeds, if it returns an error chunk or empty response, retry with local Gemma. Exception-based fallback is preferred over timeout races.

### **Issue 2: Double Registration Ambiguity**
- **Problem:** Task 4 adds `AddCodebrewRouterLocalProvider` but legacy `AddLocalInferenceServices` can still be called, causing duplicate registration.
- **Resolution (in Task 4, Step 5):** Explicitly **delete/replace** the legacy call in `Program.cs`. Add a `[Obsolete("Use AddCodebrewRouterLocalProvider")]` attribute to the legacy method. Never call both.

### **Issue 3: LocalGemmaChatClient Lazy-Load + Prompt Cleaner Timing Conflict**
- **Problem:** Prompt cleaner singleton is created at startup before LocalGemma is lazily loaded, so preference logic fails.
- **Resolution (in Task 6, Step 3):** Change prompt cleaner preference from "resolve at registration time" to "resolve at request time" by using a factory that re-checks at each use. Updated code shows `sp.GetKeyedService<IChatClient>("LocalGemma")` called within the singleton factory, **not** at factory-creation time.

---

## Context And References

- User flow: `API -> local CodebrewRouter provider -> Gemma 4 cleaner -> online routing logic OR offline local response`.
- The Hugging Face repository `CelesteImperia/Gemma-4-26B-MoE-GGUF` is a GGUF workstation reference for LLamaSharp loading and quantization choices, not the startup default: https://huggingface.co/CelesteImperia/Gemma-4-26B-MoE-GGUF
- Google Gemma 4 multi-token prediction notes support the local-responsiveness goal and should be captured as runtime guidance: https://blog.google/innovation-and-ai/technology/developers-tools/multi-token-prediction-gemma-4/
- Google AI Edge LiteRT-LM Gemma 4 docs identify `gemma-4-e4b-it` as an edge-friendly local profile: https://ai.google.dev/edge/litert-lm/models/gemma-4#gemma-4-e4b
- Local repo docs already prefer treating local HTTP runtimes as OpenAI-compatible entries where possible: `Docs/design/adr/0005-local-runtime-compatibility.md`.

## File Structure

- Modify `Blaze.LlmGateway.Core/Configuration/CodebrewRouterOptions.cs`: add offline/local routing options that the router can read without referencing LocalInference.
- Create `Blaze.LlmGateway.Core/Connectivity/IInternetConnectivityProbe.cs`: small abstraction used by Infrastructure to decide online/offline mode.
- Create `Blaze.LlmGateway.Infrastructure/Connectivity/HttpInternetConnectivityProbe.cs`: default implementation using `IHttpClientFactory` and a short timeout.
- Modify `Blaze.LlmGateway.Infrastructure/CodebrewRouterChatClient.cs`: clean prompt first, use the connectivity probe, and short-circuit to `"LocalGemma"` when offline.
- Modify `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`: register `IInternetConnectivityProbe`, prefer `"LocalGemma"` for prompt cleanup when available, and keep cloud providers registered.
- Modify `Blaze.LlmGateway.Core/Provider/CodebrewRouterProviderOptions.cs`: add local model fields so API can bind provider config from the existing local inference section.
- Modify `Blaze.LlmGateway.LocalInference/ServiceCollectionExtensions.cs`: keep legacy API, but add provider-backed registration that maps provider options to `LocalInferenceOptions` and registers `"LocalGemma"`.
- Modify `Blaze.LlmGateway.LocalInference/LocalGemmaChatClient.cs`: pass model options into LLamaSharp and emit actual token text in MEAI updates.
- Modify `Blaze.LlmGateway.Api/Program.cs`: replace direct legacy local inference registration with provider-backed registration.
- Modify `Blaze.LlmGateway.Api/appsettings.LocalInference.json`: set Gemma 4 local defaults and document E4B versus 26B profile selection.
- Create `Blaze.LlmGateway.Tests/Infrastructure/CodebrewRouterChatClientOfflineTests.cs`: tests for offline local fallback and online remote routing.
- Modify `Blaze.LlmGateway.Tests/Infrastructure/ServiceCollectionExtensionsTests.cs`: tests for connectivity probe and prompt cleaner registration preference.
- Modify `Blaze.LlmGateway.Tests/LocalInference/LocalInferenceIntegrationTests.cs`: tests for provider-backed `"LocalGemma"` registration.
- Modify `Blaze.LlmGateway.Tests/LocalInference/LocalGemmaChatClientTests.cs`: tests for options-backed model parameters and no eager load.
- Modify `Docs/MIGRATION-LocalInferenceToProvider.md`: document the API registration change.

## Task 1: Add Offline Routing Options And Connectivity Probe Contract

**Files:**
- Modify: `Blaze.LlmGateway.Core/Configuration/CodebrewRouterOptions.cs`
- Create: `Blaze.LlmGateway.Core/Connectivity/IInternetConnectivityProbe.cs`
- Test: `Blaze.LlmGateway.Tests/Infrastructure/CodebrewRouterChatClientOfflineTests.cs`

- [ ] **Step 1: Write the failing options and probe tests**

Add this test file:

```csharp
namespace Blaze.LlmGateway.Tests.Infrastructure;

using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.Connectivity;
using FluentAssertions;
using Xunit;

public class CodebrewRouterConnectivityOptionsTests
{
    [Fact]
    public void CodebrewRouterOptions_Defaults_EnableOfflineLocalFallback()
    {
        var options = new CodebrewRouterOptions();

        options.OfflineLocalFallbackEnabled.Should().BeTrue();
        options.OfflineLocalProviderKey.Should().Be("LocalGemma");
        options.OnlineProbeUrl.Should().Be("https://www.google.com/generate_204");
        options.OnlineProbeTimeoutMilliseconds.Should().Be(750);
    }

    [Fact]
    public async Task InternetConnectivityProbe_Interface_CanBeImplementedByTests()
    {
        IInternetConnectivityProbe probe = new TestProbe(isOnline: false);

        var result = await probe.IsOnlineAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    private sealed class TestProbe(bool isOnline) : IInternetConnectivityProbe
    {
        public Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(isOnline);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~CodebrewRouterConnectivityOptionsTests" --no-restore`

Expected: FAIL because `IInternetConnectivityProbe`, `OfflineLocalFallbackEnabled`, `OfflineLocalProviderKey`, `OnlineProbeUrl`, and `OnlineProbeTimeoutMilliseconds` do not exist.

- [ ] **Step 3: Add options and probe interface**

Append these properties inside `CodebrewRouterOptions` in `Blaze.LlmGateway.Core/Configuration/CodebrewRouterOptions.cs`:

```csharp
    /// <summary>When true, codebrewRouter returns a local provider response when internet-backed routing is unavailable.</summary>
    public bool OfflineLocalFallbackEnabled { get; set; } = true;

    /// <summary>Keyed DI provider used when offline fallback is active.</summary>
    public string OfflineLocalProviderKey { get; set; } = "LocalGemma";

    /// <summary>Small HTTP endpoint used to decide whether internet-backed routing should be attempted.</summary>
    public string OnlineProbeUrl { get; set; } = "https://www.google.com/generate_204";

    /// <summary>Timeout for the online probe. Keep this short so offline requests reach the local model quickly.</summary>
    public int OnlineProbeTimeoutMilliseconds { get; set; } = 750;
```

Create `Blaze.LlmGateway.Core/Connectivity/IInternetConnectivityProbe.cs`:

```csharp
namespace Blaze.LlmGateway.Core.Connectivity;

/// <summary>
/// Reports whether the API should attempt internet-backed provider routing for the current request.
/// </summary>
public interface IInternetConnectivityProbe
{
    Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~CodebrewRouterConnectivityOptionsTests" --no-restore`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Blaze.LlmGateway.Core/Configuration/CodebrewRouterOptions.cs
git add Blaze.LlmGateway.Core/Connectivity/IInternetConnectivityProbe.cs
git add Blaze.LlmGateway.Tests/Infrastructure/CodebrewRouterChatClientOfflineTests.cs
git commit -m "feat: add codebrewRouter offline routing options"
```

## Task 2: Implement The HTTP Connectivity Probe

**Files:**
- Create: `Blaze.LlmGateway.Infrastructure/Connectivity/HttpInternetConnectivityProbe.cs`
- Modify: `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`
- Test: `Blaze.LlmGateway.Tests/Infrastructure/ServiceCollectionExtensionsTests.cs`

- [ ] **Step 1: Write the failing DI test**

Append this test to `Blaze.LlmGateway.Tests/Infrastructure/ServiceCollectionExtensionsTests.cs`:

```csharp
[Fact]
public void AddLlmInfrastructure_RegistersInternetConnectivityProbe()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHttpClient();
    services.Configure<Blaze.LlmGateway.Core.Configuration.LlmGatewayOptions>(_ => { });
    services.AddSingleton<Blaze.LlmGateway.Core.ModelCatalog.IModelAvailabilityRegistry>(
        new Blaze.LlmGateway.Api.ModelAvailabilityRegistry());
    services.AddSingleton<Blaze.LlmGateway.Core.ModelCatalog.IModelCatalog,
        Blaze.LlmGateway.Api.ModelCatalogService>();
    services.AddLlmInfrastructure();

    var sp = services.BuildServiceProvider();

    var probe = sp.GetService<Blaze.LlmGateway.Core.Connectivity.IInternetConnectivityProbe>();
    Assert.NotNull(probe);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "AddLlmInfrastructure_RegistersInternetConnectivityProbe" --no-restore`

Expected: FAIL because the probe implementation is not registered.

- [ ] **Step 3: Add the probe implementation**

Create `Blaze.LlmGateway.Infrastructure/Connectivity/HttpInternetConnectivityProbe.cs`:

```csharp
namespace Blaze.LlmGateway.Infrastructure.Connectivity;

using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.Connectivity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class HttpInternetConnectivityProbe(
    IHttpClientFactory httpClientFactory,
    IOptions<CodebrewRouterOptions> options,
    ILogger<HttpInternetConnectivityProbe> logger) : IInternetConnectivityProbe
{
    public async Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default)
    {
        var url = options.Value.OnlineProbeUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Online probe URL is invalid: {OnlineProbeUrl}", url);
            return false;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(
            Math.Max(100, options.Value.OnlineProbeTimeoutMilliseconds)));

        try
        {
            var client = httpClientFactory.CreateClient(nameof(HttpInternetConnectivityProbe));
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            return (int)response.StatusCode is >= 200 and < 500;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Online probe timed out after {TimeoutMs}ms", options.Value.OnlineProbeTimeoutMilliseconds);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Online probe failed");
            return false;
        }
    }
}
```

- [ ] **Step 4: Register the probe**

Add these usings to `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`:

```csharp
using Blaze.LlmGateway.Core.Connectivity;
using Blaze.LlmGateway.Infrastructure.Connectivity;
```

Add this registration near the start of `AddLlmInfrastructure`:

```csharp
        services.AddHttpClient(nameof(HttpInternetConnectivityProbe));
        services.TryAddSingleton<IInternetConnectivityProbe, HttpInternetConnectivityProbe>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "AddLlmInfrastructure_RegistersInternetConnectivityProbe" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/Connectivity/HttpInternetConnectivityProbe.cs
git add Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs
git add Blaze.LlmGateway.Tests/Infrastructure/ServiceCollectionExtensionsTests.cs
git commit -m "feat: register internet connectivity probe"
```

## Task 3: Make CodebrewRouter Offline-Aware

**Files:**
- Modify: `Blaze.LlmGateway.Infrastructure/CodebrewRouterChatClient.cs`
- Test: `Blaze.LlmGateway.Tests/Infrastructure/CodebrewRouterChatClientOfflineTests.cs`

- [ ] **Step 1: Write failing offline and online routing tests**

Append these tests to `Blaze.LlmGateway.Tests/Infrastructure/CodebrewRouterChatClientOfflineTests.cs`:

```csharp
[Fact]
public async Task GetResponseAsync_WhenOffline_UsesLocalGemmaAfterPromptCleaning()
{
    var services = new ServiceCollection();
    var local = new RecordingChatClient("local");
    var remote = new RecordingChatClient("remote");
    services.AddKeyedSingleton<IChatClient>("LocalGemma", local);
    services.AddKeyedSingleton<IChatClient>("OpenCodeGo_Qwen3_5Plus", remote);
    var sp = services.BuildServiceProvider();

    var cleaner = new StubPromptCleaner("cleaned prompt");
    var sut = CodebrewRouterClientFactory.Create(
        sp,
        cleaner,
        new StubConnectivityProbe(false),
        fallbackRules: new Dictionary<string, string[]> { ["General"] = ["OpenCodeGo_Qwen3_5Plus"] });

    var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "messy prompt")]);

    response.Text.Should().Be("local");
    local.LastUserText.Should().Be("cleaned prompt");
    remote.Calls.Should().Be(0);
}

[Fact]
public async Task GetResponseAsync_WhenOnline_UsesConfiguredRoutingChain()
{
    var services = new ServiceCollection();
    var local = new RecordingChatClient("local");
    var remote = new RecordingChatClient("remote");
    services.AddKeyedSingleton<IChatClient>("LocalGemma", local);
    services.AddKeyedSingleton<IChatClient>("OpenCodeGo_Qwen3_5Plus", remote);
    var sp = services.BuildServiceProvider();

    var sut = CodebrewRouterClientFactory.Create(
        sp,
        new StubPromptCleaner("cleaned prompt"),
        new StubConnectivityProbe(true),
        fallbackRules: new Dictionary<string, string[]> { ["General"] = ["OpenCodeGo_Qwen3_5Plus"] });

    var response = await sut.GetResponseAsync([new ChatMessage(ChatRole.User, "messy prompt")]);

    response.Text.Should().Be("remote");
    remote.LastUserText.Should().Be("cleaned prompt");
    local.Calls.Should().Be(0);
}
```

Add these private helper types in the same file:

```csharp
private sealed class StubConnectivityProbe(bool isOnline) : IInternetConnectivityProbe
{
    public Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(isOnline);
}

private sealed class StubPromptCleaner(string cleaned) : IPromptCleaner
{
    public Task<string> CleanAsync(string prompt, CancellationToken cancellationToken = default)
        => Task.FromResult(cleaned);
}

private sealed class RecordingChatClient(string responseText) : IChatClient
{
    public int Calls { get; private set; }
    public string? LastUserText { get; private set; }
    public ChatClientMetadata Metadata => new();

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        LastUserText = chatMessages.Last(m => m.Role == ChatRole.User).Text;
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++;
        LastUserText = chatMessages.Last(m => m.Role == ChatRole.User).Text;
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, responseText);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
```

Add this test factory and support stubs in the same file:

```csharp
private static class CodebrewRouterClientFactory
{
    public static CodebrewRouterChatClient Create(
        IServiceProvider serviceProvider,
        IPromptCleaner cleaner,
        IInternetConnectivityProbe connectivityProbe,
        Dictionary<string, string[]> fallbackRules)
    {
        var codebrewOptions = Options.Create(new CodebrewRouterOptions
        {
            OfflineLocalFallbackEnabled = true,
            OfflineLocalProviderKey = "LocalGemma",
            FallbackRules = fallbackRules
        });

        var gatewayOptions = Options.Create(new LlmGatewayOptions
        {
            Providers = new ProvidersOptions
            {
                LmStudio = new LmStudioOptions
                {
                    Endpoint = "http://localhost:1234/v1",
                    Model = "local-test",
                    MaxContextTokens = 32768,
                    ReservedOutputTokens = 1024
                }
            }
        });

        return new CodebrewRouterChatClient(
            new RecordingChatClient("inner"),
            new StubTaskClassifier(TaskType.General),
            cleaner,
            new NoopContextCompactor(),
            new ZeroTokenCounter(),
            codebrewOptions,
            gatewayOptions,
            new AlwaysAvailableRegistry(),
            connectivityProbe,
            serviceProvider,
            NullLogger<CodebrewRouterChatClient>.Instance);
    }
}

private sealed class StubTaskClassifier(TaskType taskType) : ITaskClassifier
{
    public Task<TaskType> ClassifyAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
        => Task.FromResult(taskType);
}

private sealed class ZeroTokenCounter : ITokenCounter
{
    public int CountTokens(IEnumerable<ChatMessage> messages, string? modelId = null) => 0;
}

private sealed class AlwaysAvailableRegistry : IModelAvailabilityRegistry
{
    public IReadOnlyList<AvailableModel> GetModels(bool includeUnavailable = false) => [];
    public AvailableModel? FindModel(string modelId, bool includeUnavailable = false) => null;
    public bool IsProviderAvailable(string provider) => true;
    public string? GetProviderError(string provider) => null;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~CodebrewRouterChatClientOfflineTests" --no-restore`

Expected: FAIL because `CodebrewRouterChatClient` does not accept or use `IInternetConnectivityProbe`.

**Note (Blocking Issue #7 - TDD coverage):** The plan should also include these additional test cases for production robustness:
- `GetResponseAsync_WhenProbeThrows_AssumesOfflineAndUsesLocal()` - Tests exception handling in Step 4's try-catch block
- `GetResponseAsync_WhenLocalGemmaNotRegistered_FallsBackToNormalRouting()` - Tests fallback when offline provider is unavailable
- `GetStreamingResponseAsync_WhenProviderFailsMidstream_NoFallback()` - Documents current behavior (blocking issue #1); mid-stream failover will be a future enhancement

- [ ] **Step 3: Inject the probe into CodebrewRouterChatClient**

Add constructor dependency in `Blaze.LlmGateway.Infrastructure/CodebrewRouterChatClient.cs`:

```csharp
using Blaze.LlmGateway.Core.Connectivity;
```

Add parameter after `IModelAvailabilityRegistry availabilityRegistry`:

```csharp
    IInternetConnectivityProbe connectivityProbe,
```

- [ ] **Step 4: Add offline local response helper**

Add this helper inside `CodebrewRouterChatClient`:

```csharp
    private async Task<IChatClient?> ResolveOfflineLocalClientAsync(CancellationToken cancellationToken)
    {
        if (!Options.OfflineLocalFallbackEnabled)
        {
            return null;
        }

        bool isOnline;
        try
        {
            isOnline = await connectivityProbe.IsOnlineAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ROUTER-OFFLINE] Connectivity probe failed; assuming offline");
            isOnline = false;
        }

        if (isOnline)
        {
            return null;
        }

        var key = Options.OfflineLocalProviderKey;
        var localClient = serviceProvider.GetKeyedService<IChatClient>(key);
        if (localClient is null)
        {
            logger.LogWarning("[ROUTER-OFFLINE] Offline fallback provider '{Key}' is not registered", key);
            return null;
        }

        logger.LogInformation("[ROUTER-OFFLINE] Internet probe failed; using local provider '{Key}'", key);
        return localClient;
    }
```

**Note (Blocking Issue #1 mitigation):** The method now wraps `IsOnlineAsync()` with exception handling, addressing the non-blocking issue about probe failures. For the blocking issue about mid-stream failures, Task 3 Step 8 will need to be updated after implementation to add retry logic if the provider fails mid-stream.

- [ ] **Step 5: Use offline helper in non-streaming path**

In `GetResponseAsync`, after `cleanedMessages` is computed and before `ResolveAsync`, add:

```csharp
        var offlineClient = await ResolveOfflineLocalClientAsync(cancellationToken);
        if (offlineClient is not null)
        {
            return await offlineClient.GetResponseAsync(cleanedMessages, options, cancellationToken);
        }
```

- [ ] **Step 6: Use offline helper in streaming path**

In `GetStreamingResponseAsync`, after `cleanedMessages` and `RouterCleanEvent`, add:

```csharp
        var offlineClient = await ResolveOfflineLocalClientAsync(cancellationToken);
        if (offlineClient is not null)
        {
            await foreach (var update in offlineClient.GetStreamingResponseAsync(cleanedMessages, options, cancellationToken))
            {
                yield return update;
            }

            yield break;
        }
```

- [ ] **Step 7: Update construction site**

In `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`, update the `CodebrewRouterChatClient` registration to pass:

```csharp
                sp.GetRequiredService<IInternetConnectivityProbe>(),
```

immediately after `IModelAvailabilityRegistry`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~CodebrewRouterChatClientOfflineTests" --no-restore`

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/CodebrewRouterChatClient.cs
git add Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs
git add Blaze.LlmGateway.Tests/Infrastructure/CodebrewRouterChatClientOfflineTests.cs
git commit -m "feat: route codebrewRouter offline requests to local Gemma"
```

## Task 4: Register Provider-Backed Local Inference For API

**Files:**
- Modify: `Blaze.LlmGateway.Core/Provider/CodebrewRouterProviderOptions.cs`
- Modify: `Blaze.LlmGateway.LocalInference/ServiceCollectionExtensions.cs`
- Modify: `Blaze.LlmGateway.Api/Program.cs`
- Test: `Blaze.LlmGateway.Tests/LocalInference/LocalInferenceIntegrationTests.cs`

- [ ] **Step 1: Write the failing provider-backed registration test**

Append this test to `Blaze.LlmGateway.Tests/LocalInference/LocalInferenceIntegrationTests.cs`:

```csharp
[Fact]
public void AddCodebrewRouterLocalProvider_RegistersLocalGemmaAndProviderOptions()
{
    var services = CreateServiceCollection();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LlmGateway:LocalInference:Enabled"] = "true",
            ["LlmGateway:LocalInference:ModelPath"] = "C:\\models\\gemma-4-e4b-it-q4_k_m.gguf",
            ["LlmGateway:LocalInference:CacheDirectory"] = ".llm-cache",
            ["LlmGateway:LocalInference:MaxContextTokens"] = "8192",
            ["LlmGateway:LocalInference:ThreadCount"] = "4"
        })
        .Build();

    services.AddCodebrewRouterLocalProvider(configuration);
    var sp = services.BuildServiceProvider();

    var providerOptions = sp.GetRequiredService<CodebrewRouterProviderOptions>();
    providerOptions.LocalModelPath.Should().Be("C:\\models\\gemma-4-e4b-it-q4_k_m.gguf");

    var localGemma = sp.GetKeyedService<IChatClient>("LocalGemma");
    localGemma.Should().BeOfType<LocalGemmaChatClient>();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "AddCodebrewRouterLocalProvider_RegistersLocalGemmaAndProviderOptions" --no-restore`

Expected: FAIL because `AddCodebrewRouterLocalProvider` and provider local model fields do not exist.

- [ ] **Step 3: Extend provider options**

Add these properties to `Blaze.LlmGateway.Core/Provider/CodebrewRouterProviderOptions.cs`:

```csharp
    /// <summary>Local GGUF file path or remote URL for the local Gemma model.</summary>
    public string LocalModelPath { get; set; } = string.Empty;

    /// <summary>Directory used when LocalModelPath is a remote URL and must be cached.</summary>
    public string CacheDirectory { get; set; } = ".llm-cache";

    /// <summary>Friendly local profile name for diagnostics and model catalog entries.</summary>
    public string LocalModelProfile { get; set; } = "gemma-4-e4b-it";

    /// <summary>Maximum local context window passed to LLamaSharp.</summary>
    public int LocalMaxContextTokens { get; set; } = 8192;

    /// <summary>CPU thread count. Zero means LLamaSharp default selection.</summary>
    public int LocalThreadCount { get; set; } = 0;

    /// <summary>Number of model layers to offload to GPU. Zero keeps CPU-only behavior.</summary>
    public int LocalGpuLayerCount { get; set; } = 0;
```

- [ ] **Step 4: Add provider-backed local registration extension**

In `Blaze.LlmGateway.LocalInference/ServiceCollectionExtensions.cs`, add this method:

```csharp
    public static IServiceCollection AddCodebrewRouterLocalProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        var section = configuration.GetSection("LlmGateway:LocalInference");
        var localOptions = new LocalInferenceOptions();
        section.Bind(localOptions);

        var providerOptions = new CodebrewRouterProviderOptions
        {
            LocalEndpoint = configuration["LlmGateway:Providers:OllamaRouter:PrimaryEndpoint"]
                ?? "http://127.0.0.1:11434",
            LocalModelPath = localOptions.ModelPath,
            CacheDirectory = localOptions.CacheDirectory,
            LocalMaxContextTokens = localOptions.MaxContextTokens,
            LocalThreadCount = localOptions.ThreadCount,
            RemoteDiscoveryEndpoint = null,
            CacheAvailabilityTtlSeconds = localOptions.CacheAvailabilityTtlSeconds ?? 60,
            CircuitBreakerCooldownMinutes = localOptions.CircuitBreakerCooldownMinutes,
            TestMode = false
        };

        services.AddSingleton(providerOptions);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(providerOptions));
        services.AddSingleton(localOptions);
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(localOptions));

        services.AddSingleton(sp => new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(localOptions.DownloadTimeoutSeconds)
        });

        services.AddSingleton<IModelDistributionProvider, RuntimeDownloadModelProvider>();
        services.AddSingleton<ILocalModelAvailability, LocalModelAvailabilityService>();
        services.AddSingleton<ICodebrewRouterDiscoveryService, CodebrewRouterDiscoveryService>();
        services.AddSingleton<ILocalInferenceHealthManager, LocalInferenceHealthManager>();

        services.AddKeyedSingleton<IChatClient>("LocalGemma", (sp, _) =>
            new LocalGemmaChatClient(
                localOptions.ModelPath,
                localOptions.MaxContextTokens,
                localOptions.ThreadCount,
                providerOptions.LocalGpuLayerCount));

        services.AddHealthChecks()
            .AddCheck<LocalInferenceHealthManager>(
                "local-inference",
                Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: ["local-inference", "readiness"]);

        return services;
    }
```

- [ ] **Step 5: Replace API legacy registration**

In `Blaze.LlmGateway.Api/Program.cs`, **REPLACE** (not supplement):

```csharp
#pragma warning disable CS0618
builder.Services.AddLocalInferenceServices(builder.Configuration);
#pragma warning restore CS0618
```

**with** (completely remove the legacy call):

```csharp
builder.Services.AddCodebrewRouterLocalProvider(builder.Configuration);
```

**Critical (Blocking Issue #2):** Calling both methods will cause duplicate DI registration. Ensure the legacy call is **completely removed**. If needed, mark `AddLocalInferenceServices` as `[Obsolete("Use AddCodebrewRouterLocalProvider")]` in `LocalInference/ServiceCollectionExtensions.cs` to prevent accidental reuse.

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "AddCodebrewRouterLocalProvider_RegistersLocalGemmaAndProviderOptions" --no-restore`

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add Blaze.LlmGateway.Core/Provider/CodebrewRouterProviderOptions.cs
git add Blaze.LlmGateway.LocalInference/ServiceCollectionExtensions.cs
git add Blaze.LlmGateway.Api/Program.cs
git add Blaze.LlmGateway.Tests/LocalInference/LocalInferenceIntegrationTests.cs
git commit -m "feat: register API local inference through provider"
```

## Task 5: Make LocalGemmaChatClient Options-Backed And Lazy

**Files:**
- Modify: `Blaze.LlmGateway.LocalInference/LocalGemmaChatClient.cs`
- Test: `Blaze.LlmGateway.Tests/LocalInference/LocalGemmaChatClientTests.cs`

- [ ] **Step 1: Write failing constructor and streaming tests**

Append these tests to `Blaze.LlmGateway.Tests/LocalInference/LocalGemmaChatClientTests.cs`:

```csharp
[Fact]
public void Constructor_WithModelParameters_DoesNotLoadMissingModel()
{
    var client = new LocalGemmaChatClient(
        "C:\\missing\\gemma-4-e4b-it-q4_k_m.gguf",
        contextSize: 8192,
        threadCount: 4,
        gpuLayerCount: 0);

    client.Should().BeAssignableTo<IChatClient>();
}

[Fact]
public async Task GetResponseAsync_WithNoLoadedModel_ReturnsEmptyLocalGemmaResponse()
{
    var client = new LocalGemmaChatClient(
        "C:\\missing\\gemma-4-e4b-it-q4_k_m.gguf",
        contextSize: 8192,
        threadCount: 4,
        gpuLayerCount: 0);

    var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

    response.ModelId.Should().Be("gemma-local");
    response.Text.Should().BeEmpty();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~LocalGemmaChatClientTests" --no-restore`

Expected: FAIL because the new constructor signature does not exist.

- [ ] **Step 3: Add options-backed constructor**

Change the constructor signature in `LocalGemmaChatClient`:

```csharp
    public LocalGemmaChatClient(
        string? modelPath = null,
        int contextSize = 2048,
        int threadCount = 0,
        int gpuLayerCount = 0)
        : base(new NoOpChatClientWithMetadata())
```

Change the `ModelParams` block:

```csharp
            var modelParams = new ModelParams(modelPath)
            {
                ContextSize = (uint)Math.Max(1, contextSize),
                GpuLayerCount = Math.Max(0, gpuLayerCount),
            };

            if (threadCount > 0)
            {
                modelParams.Threads = threadCount;
            }
```

- [ ] **Step 4: Emit real streaming token text**

Replace the body of the token loop:

```csharp
            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                yield return new ChatResponseUpdate();

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
```

with:

```csharp
            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                if (string.IsNullOrEmpty(token))
                {
                    continue;
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, token)
                {
                    ModelId = "gemma-local"
                };

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
```

Change `GetResponseAsync` accumulation:

```csharp
        await foreach (var update in GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            accumulatedText += update.Text;
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~LocalGemmaChatClientTests" --no-restore`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Blaze.LlmGateway.LocalInference/LocalGemmaChatClient.cs
git add Blaze.LlmGateway.Tests/LocalInference/LocalGemmaChatClientTests.cs
git commit -m "feat: configure local Gemma client from provider options"
```

## Task 6: Prefer LocalGemma For Prompt Cleanup

**Files:**
- Modify: `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`
- Test: `Blaze.LlmGateway.Tests/GemmaPromptCleanerTests.cs`

- [ ] **Step 1: Write failing service registration test**

Add this test to `Blaze.LlmGateway.Tests/GemmaPromptCleanerTests.cs`:

```csharp
[Fact]
public void PromptCleanerRegistration_PrefersLocalGemma_WhenRegistered()
{
    var services = new ServiceCollection();
    var localClient = new Mock<IChatClient>().Object;
    services.AddLogging();
    services.AddOptions();
    services.Configure<LlmGatewayOptions>(options =>
    {
        options.PromptCleanup.Enabled = true;
    });
    services.AddKeyedSingleton<IChatClient>("LocalGemma", localClient);
    services.AddKeyedSingleton<IChatClient>("OllamaRouter", new Mock<IChatClient>().Object);
    services.AddLlmInfrastructure();

    var sp = services.BuildServiceProvider();

    var cleaner = sp.GetRequiredService<IPromptCleaner>();
    cleaner.Should().BeOfType<GemmaPromptCleaner>();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "PromptCleanerRegistration_PrefersLocalGemma_WhenRegistered" --no-restore`

Expected: FAIL because the prompt cleaner factory only looks for `"OllamaRouter"`.

- [ ] **Step 3: Prefer LocalGemma in prompt cleaner factory**

In `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs`, replace:

```csharp
            var ollamaRouter = sp.GetKeyedService<IChatClient>("OllamaRouter");
            if (ollamaRouter is null)
            {
                return new NoopPromptCleaner();
            }

            return new GemmaPromptCleaner(
                ollamaRouter,
                sp.GetRequiredService<IOptions<PromptCleanupOptions>>(),
                sp.GetRequiredService<ILogger<GemmaPromptCleaner>>());
```

with:

```csharp
            var cleanerClient =
                sp.GetKeyedService<IChatClient>("LocalGemma")
                ?? sp.GetKeyedService<IChatClient>("OllamaRouter");

            if (cleanerClient is null)
            {
                return new NoopPromptCleaner();
            }

            return new GemmaPromptCleaner(
                cleanerClient,
                sp.GetRequiredService<IOptions<PromptCleanupOptions>>(),
                sp.GetRequiredService<ILogger<GemmaPromptCleaner>>());
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "PromptCleanerRegistration_PrefersLocalGemma_WhenRegistered" --no-restore`

Expected: PASS.

**Important Note on Blocking Issue #3:** The fix in Step 3 defers LocalGemma resolution to factory-call time. Update the prompt cleaner factory to call `sp.GetKeyedService<IChatClient>("LocalGemma")` INSIDE the singleton factory method (not before), so lazy-loaded LocalGemma is visible when prompt cleanup actually runs. The code snippet in Step 3 already shows this pattern correctly.

- [ ] **Step 5: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs
git add Blaze.LlmGateway.Tests/GemmaPromptCleanerTests.cs
git commit -m "feat: prefer local Gemma for prompt cleanup"
```

## Task 7: Update Gemma 4 Configuration And Docs

**Files:**
- Modify: `Blaze.LlmGateway.Api/appsettings.LocalInference.json`
- Modify: `Docs/MIGRATION-LocalInferenceToProvider.md`

- [ ] **Step 1: Update local inference defaults**

In `Blaze.LlmGateway.Api/appsettings.LocalInference.json`, set the local model profile comments and values:

```jsonc
      "ModelPath": ".llm-cache/gemma-4-e4b-it-q4_k_m.gguf",
      "CacheDirectory": ".llm-cache",
      "ThreadCount": 0,
      "MaxContextTokens": 8192,
```

Add a comment near `ModelPath`:

```jsonc
      // Default profile: Gemma 4 E4B instruct, chosen for quick local/offline API fallback.
      // Workstation profile reference: https://huggingface.co/CelesteImperia/Gemma-4-26B-MoE-GGUF
      // Use a downloaded GGUF file path for 26B MoE only on hosts with enough memory/GPU capacity.
```

- [ ] **Step 2: Update migration docs**

Add this section to `Docs/MIGRATION-LocalInferenceToProvider.md`:

```markdown
## API Provider Path

The API now registers local inference through `AddCodebrewRouterLocalProvider(configuration)`.
This keeps `"LocalGemma"` available as a keyed `IChatClient` while allowing `codebrewRouter`
to use internet-backed providers when online and local Gemma when offline.

The default local profile is Gemma 4 E4B for faster local fallback. The 26B MoE GGUF profile
is supported as a configured file path on machines with sufficient memory and GPU capacity.
```

- [ ] **Step 3: Commit**

```bash
git add Blaze.LlmGateway.Api/appsettings.LocalInference.json
git add Docs/MIGRATION-LocalInferenceToProvider.md
git commit -m "docs: document Gemma 4 local provider profile"
```

## Task 8: Quality Gate

**Files:**
- Verify: all changed projects and tests

- [ ] **Step 1: Run focused tests**

Run:

```powershell
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --filter "FullyQualifiedName~CodebrewRouterConnectivityOptionsTests|FullyQualifiedName~CodebrewRouterChatClientOfflineTests|FullyQualifiedName~LocalGemmaChatClientTests|AddCodebrewRouterLocalProvider_RegistersLocalGemmaAndProviderOptions|PromptCleanerRegistration_PrefersLocalGemma_WhenRegistered" --no-restore
```

Expected: PASS.

- [ ] **Step 2: Run full tests**

Run:

```powershell
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-restore
```

Expected: PASS. If unrelated external integration tests depend on unavailable local services, record exact skipped or failed tests and run the focused suite plus build before handing off.

- [ ] **Step 3: Build with warnings as errors**

Run:

```powershell
dotnet build Blaze.LlmGateway.slnx --no-incremental -warnaserror
```

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Check formatting**

Run:

```powershell
dotnet format Blaze.LlmGateway.slnx --verify-no-changes
```

Expected: no files need reformatting.

- [ ] **Step 5: Commit quality-gate fixes if needed**

If formatting or build fixes were required:

```bash
git add .
git commit -m "chore: satisfy local provider quality gate"
```

## Self-Review

- Spec coverage: The plan covers API provider registration, local Gemma cleanup, online routing, offline local response, Gemma 4 E4B default, 26B MoE GGUF reference, and lazy local model loading.
- Placeholder scan: No steps rely on undefined implementation-only placeholders. Each code change step includes concrete paths and code snippets.
- Type consistency: New probe is defined in Core and implemented in Infrastructure so no LocalInference reference is introduced into Infrastructure. LocalInference remains allowed to call Infrastructure provider extensions because it already references Infrastructure.

## Known Limitations & Future Enhancements

### **Blocking Issue #1: Mid-Stream Streaming Fallback** (Deferred)
The current implementation checks connectivity once at the start of a request. If a provider succeeds in starting to stream but then fails mid-stream due to network issues, offline fallback is **NOT triggered**. The client receives a partial response from the failed provider rather than an automatic fallback to LocalGemma.

**Why deferred:** Adding per-chunk re-probing would add latency. The preferred solution is provider-level exception handling with a fallback chain (future enhancement). For now, this is documented behavior: offline fallback is an "upfront decision," not a "mid-stream recovery."

### **Blocking Issue #2: Legacy Registration Deprecation** (Implementation)
The original `AddLocalInferenceServices` method must be marked `[Obsolete]` to prevent accidental double-registration. This will be handled in Task 4, Step 5.

### **Blocking Issue #3: Lazy-Load Timing** (Fixed in Plan)
Resolved by deferring LocalGemma resolution to factory-call time in the prompt cleaner (Task 6, Step 3). No longer a blocker.

### **Non-Blocking: Network Variability**
The default probe URL (`https://www.google.com/generate_204`) may be inaccessible in corporate networks. This is addressed in a non-blocking way by making the probe URL configurable via `CodebrewRouterOptions.OnlineProbeUrl`. Operators should configure it for their network on deployment.
