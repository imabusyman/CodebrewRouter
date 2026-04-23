using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.TaskRouting;
using Blaze.LlmGateway.Infrastructure;
using Blaze.LlmGateway.Infrastructure.TaskClassification;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

public class CodebrewRouterChatClientTests
{
    // ── Shared helpers ────────────────────────────────────────────────────────

    private static List<ChatMessage> UserMessages(string text) =>
        [new ChatMessage(ChatRole.User, text)];

    private static ChatResponse TextResponse(string text) =>
        new([new ChatMessage(ChatRole.Assistant, text)]);

    private static Mock<IChatClient> ClientReturning(string text)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextResponse(text));
        return mock;
    }

    private static Mock<IChatClient> ClientThrowing()
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Provider unavailable"));
        return mock;
    }

    private static Mock<ITaskClassifier> ClassifierFor(TaskType taskType)
    {
        var mock = new Mock<ITaskClassifier>();
        mock.Setup(c => c.ClassifyAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskType);
        return mock;
    }

    private static CodebrewRouterChatClient CreateRouter(
        IChatClient innerClient,
        ITaskClassifier classifier,
        IServiceProvider serviceProvider,
        CodebrewRouterOptions? options = null)
    {
        var opts = Options.Create(options ?? new CodebrewRouterOptions());
        var logger = new Mock<ILogger<CodebrewRouterChatClient>>().Object;
        return new CodebrewRouterChatClient(innerClient, classifier, opts, serviceProvider, logger);
    }

    // Streaming helpers — static async iterators for test streams

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamOf(
        params ChatResponseUpdate[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask; // satisfy async requirement
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowingStream()
    {
        await Task.Yield(); // make it truly async so throw happens on MoveNextAsync
        throw new InvalidOperationException("Provider stream failed on first chunk");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static Mock<IChatClient> ClientStreaming(params ChatResponseUpdate[] updates)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamOf(updates));
        return mock;
    }

    private static Mock<IChatClient> ClientStreamingThrows()
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingStream());
        return mock;
    }

    // ── Non-streaming: provider selection ─────────────────────────────────────

    [Fact]
    public async Task UsesFirstProvider_WhenItSucceeds()
    {
        var primary   = ClientReturning("Primary");
        var secondary = ClientReturning("Secondary");

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P1", primary.Object)
            .AddKeyedSingleton<IChatClient>("P2", secondary.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["P1", "P2"] }
        };

        var router = CreateRouter(new Mock<IChatClient>().Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("hello"));

        Assert.Equal("Primary", result.Text);
        primary.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        secondary.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TriesSecondProvider_WhenFirstFails()
    {
        var primary   = ClientThrowing();
        var secondary = ClientReturning("Secondary");

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P1", primary.Object)
            .AddKeyedSingleton<IChatClient>("P2", secondary.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["P1", "P2"] }
        };

        var router = CreateRouter(new Mock<IChatClient>().Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("hello"));

        Assert.Equal("Secondary", result.Text);
        primary.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        secondary.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FallsBackToInnerClient_WhenAllProvidersFail()
    {
        var primary     = ClientThrowing();
        var innerClient = ClientReturning("InnerFallback");

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P1", primary.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["P1"] }
        };

        var router = CreateRouter(innerClient.Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("hello"));

        Assert.Equal("InnerFallback", result.Text);
        innerClient.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SkipsUnregisteredProvider_AndTriesNext()
    {
        var secondary = ClientReturning("Secondary");

        // "NonExistent" is NOT registered in DI
        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P2", secondary.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["NonExistent", "P2"] }
        };

        var router = CreateRouter(new Mock<IChatClient>().Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("hello"));

        Assert.Equal("Secondary", result.Text);
    }

    [Fact]
    public async Task FallsBackToInnerClient_WhenChainIsEmpty()
    {
        var innerClient = ClientReturning("InnerFallback");

        var sp = new ServiceCollection().BuildServiceProvider();
        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = [] }
        };

        var router = CreateRouter(innerClient.Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("hello"));

        Assert.Equal("InnerFallback", result.Text);
    }

    [Fact]
    public async Task UsesGeneralChain_WhenTaskTypeHasNoMatchingRule()
    {
        // Classifier says Coding, but options only define General
        var generalProvider = ClientReturning("General");

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("GP", generalProvider.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["GP"] }
            // "Coding" is absent → should fall back to General
        };

        var router = CreateRouter(new Mock<IChatClient>().Object, ClassifierFor(TaskType.Coding).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("write code"));

        Assert.Equal("General", result.Text);
    }

    [Fact]
    public async Task FallsBackToInnerClient_WhenNoRulesAndNoGeneral()
    {
        // No fallback rules at all
        var innerClient = ClientReturning("Inner");

        var sp = new ServiceCollection().BuildServiceProvider();
        var opts = new CodebrewRouterOptions { FallbackRules = new(StringComparer.OrdinalIgnoreCase) };

        var router = CreateRouter(innerClient.Object, ClassifierFor(TaskType.Coding).Object, sp, opts);
        var result = await router.GetResponseAsync(UserMessages("write code"));

        Assert.Equal("Inner", result.Text);
    }

    // ── Streaming: basic success ───────────────────────────────────────────────

    [Fact]
    public async Task Streaming_YieldsAllChunks_WhenFirstProviderSucceeds()
    {
        var u1 = new ChatResponseUpdate(ChatRole.Assistant, "Hello");
        var u2 = new ChatResponseUpdate(ChatRole.Assistant, " World");

        var provider = ClientStreaming(u1, u2);

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P1", provider.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["P1"] }
        };

        var router = CreateRouter(new Mock<IChatClient>().Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var chunks = new List<ChatResponseUpdate>();
        await foreach (var chunk in router.GetStreamingResponseAsync(UserMessages("hello")))
            chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Hello", chunks[0].Text);
        Assert.Equal(" World", chunks[1].Text);
    }

    // ── Streaming: fallback on first-chunk failure ────────────────────────────

    [Fact]
    public async Task Streaming_TriesNextProvider_WhenFirstFailsOnFirstChunk()
    {
        var successUpdate = new ChatResponseUpdate(ChatRole.Assistant, "From P2");

        var failingProvider  = ClientStreamingThrows();
        var successProvider  = ClientStreaming(successUpdate);
        var innerClient      = new Mock<IChatClient>();

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P1", failingProvider.Object)
            .AddKeyedSingleton<IChatClient>("P2", successProvider.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["P1", "P2"] }
        };

        var router = CreateRouter(innerClient.Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var chunks = new List<ChatResponseUpdate>();
        await foreach (var chunk in router.GetStreamingResponseAsync(UserMessages("hello")))
            chunks.Add(chunk);

        Assert.Single(chunks);
        Assert.Equal("From P2", chunks[0].Text);

        // Inner client should NOT have been called
        innerClient.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Streaming: all providers fail → InnerClient ───────────────────────────

    [Fact]
    public async Task Streaming_FallsBackToInnerClient_WhenAllProvidersFail()
    {
        var innerUpdate = new ChatResponseUpdate(ChatRole.Assistant, "InnerStream");

        var failingProvider = ClientStreamingThrows();
        var innerClient     = new Mock<IChatClient>();
        innerClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamOf(innerUpdate));

        var sp = new ServiceCollection()
            .AddKeyedSingleton<IChatClient>("P1", failingProvider.Object)
            .BuildServiceProvider();

        var opts = new CodebrewRouterOptions
        {
            FallbackRules = new(StringComparer.OrdinalIgnoreCase) { ["General"] = ["P1"] }
        };

        var router = CreateRouter(innerClient.Object, ClassifierFor(TaskType.General).Object, sp, opts);
        var chunks = new List<ChatResponseUpdate>();
        await foreach (var chunk in router.GetStreamingResponseAsync(UserMessages("hello")))
            chunks.Add(chunk);

        Assert.Single(chunks);
        Assert.Equal("InnerStream", chunks[0].Text);
        innerClient.Verify(c => c.GetStreamingResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
