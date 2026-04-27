using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Blaze.LlmGateway.Api;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Infrastructure;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;

namespace Blaze.LlmGateway.Tests;

public class LlmRoutingChatClientTests
{
    private static IModelAvailabilityRegistry CreateAvailabilityRegistry(params (string Provider, bool Enabled, string? Error)[] providers)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var registry = new ModelAvailabilityRegistry();
        registry.UpdateSnapshot(
            [],
            providers.Select(provider => new ProviderAvailabilitySnapshot(provider.Provider, provider.Enabled, provider.Error, checkedAt)));
        return registry;
    }

    // ── Streaming helpers ─────────────────────────────────────────────────────

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamOf(
        params ChatResponseUpdate[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Simulates the AggregateException thrown by System.ClientModel retry policy
    /// when FoundryLocal is unreachable at 127.0.0.1:58484.
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> FoundryLocalConnectionRefusedStream()
    {
        await Task.Yield();
        throw new AggregateException(
            "One or more errors occurred. (Connection refused (127.0.0.1:58484))",
            new HttpRequestException("Connection refused (127.0.0.1:58484)"));
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static Mock<IChatClient> ClientStreamingReturning(params ChatResponseUpdate[] updates)
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(StreamOf(updates));
        return mock;
    }

    private static Mock<IChatClient> ClientStreamingThrowsAggregateException()
    {
        var mock = new Mock<IChatClient>();
        mock.Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(FoundryLocalConnectionRefusedStream());
        return mock;
    }

    // ── Non-streaming tests ───────────────────────────────────────────────────

    [Fact]
    public async Task RoutesToOllamaLocal_WhenStrategyResolvesOllamaLocal()
    {
        // Arrange
        var mockFoundryLocalClient = new Mock<IChatClient>();
        mockFoundryLocalClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, "FoundryLocal Response") }));

        var mockInnerClient = new Mock<IChatClient>();
        var mockLogger = new Mock<ILogger<LlmRoutingChatClient>>();
        var mockStrategy = new Mock<IRoutingStrategy>();
        mockStrategy
            .Setup(s => s.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.FoundryLocal);

        var mockFailoverStrategy = new Mock<IFailoverStrategy>();
        mockFailoverStrategy.Setup(f => f.GetFailoverChainAsync(It.IsAny<RouteDestination>())).Returns(new List<RouteDestination>());
        
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>("FoundryLocal", mockFoundryLocalClient.Object);
        var serviceProvider = services.BuildServiceProvider();

        var router = new LlmRoutingChatClient(
            mockInnerClient.Object,
            serviceProvider,
            mockStrategy.Object,
            mockFailoverStrategy.Object,
            CreateAvailabilityRegistry(("FoundryLocal", true, null)),
            mockLogger.Object);
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "Hello foundry") };

        // Act
        var result = await router.GetResponseAsync(messages);

        // Assert
        Assert.Equal("FoundryLocal Response", result.Text);
        mockFoundryLocalClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FallsBackToInnerClient_WhenKeyedClientNotFound()
    {
        // Arrange
        var mockInnerClient = new Mock<IChatClient>();
        mockInnerClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, "Fallback Response") }));

        var mockLogger = new Mock<ILogger<LlmRoutingChatClient>>();
        var mockFailoverStrategy = new Mock<IFailoverStrategy>();
        mockFailoverStrategy.Setup(f => f.GetFailoverChainAsync(It.IsAny<RouteDestination>())).Returns(new List<RouteDestination>());
        
        var mockStrategy = new Mock<IRoutingStrategy>();
        mockStrategy
            .Setup(s => s.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.GithubModels);

        // Empty DI — no keyed GithubModels client registered
        var serviceProvider = new ServiceCollection().BuildServiceProvider();

        var router = new LlmRoutingChatClient(
            mockInnerClient.Object,
            serviceProvider,
            mockStrategy.Object,
            mockFailoverStrategy.Object,
            CreateAvailabilityRegistry(("GithubModels", true, null)),
            mockLogger.Object);
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "What is 2+2?") };

        // Act
        var result = await router.GetResponseAsync(messages);

        // Assert
        Assert.Equal("Fallback Response", result.Text);
        mockInnerClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Streaming: FoundryLocal connection-refused failover ───────────────────

    /// <summary>
    /// Regression test for: System.AggregateException from System.ClientModel retry policy
    /// (connection refused to 127.0.0.1:58484) leaking when FoundryLocal is unreachable.
    /// Expected: TryFailoverStreamingAsync probes each fallback with a first-chunk probe,
    /// catches the AggregateException, and transparently falls over to the next provider.
    /// </summary>
    [Fact]
    public async Task Streaming_FailsOver_WhenFoundryLocalThrowsAggregateExceptionBeforeFirstChunk()
    {
        // Arrange — FoundryLocal (primary) throws the same AggregateException as System.ClientModel
        var primaryClient = ClientStreamingThrowsAggregateException();  // FoundryLocal unreachable
        var successUpdate = new ChatResponseUpdate(ChatRole.Assistant, "AzureFoundry response");
        var fallbackClient = ClientStreamingReturning(successUpdate);  // AzureFoundry ok

        var mockStrategy = new Mock<IRoutingStrategy>();
        mockStrategy
            .Setup(s => s.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.FoundryLocal);

        var mockFailoverStrategy = new Mock<IFailoverStrategy>();
        mockFailoverStrategy
            .Setup(f => f.GetFailoverChainAsync(RouteDestination.FoundryLocal))
            .Returns(new List<RouteDestination> { RouteDestination.AzureFoundry });

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>("FoundryLocal", primaryClient.Object);
        services.AddKeyedSingleton<IChatClient>("AzureFoundry", fallbackClient.Object);
        var serviceProvider = services.BuildServiceProvider();

        var mockInnerClient = new Mock<IChatClient>();
        var mockLogger = new Mock<ILogger<LlmRoutingChatClient>>();
        var router = new LlmRoutingChatClient(
            mockInnerClient.Object, serviceProvider, mockStrategy.Object,
            mockFailoverStrategy.Object, CreateAvailabilityRegistry(("FoundryLocal", true, null), ("AzureFoundry", true, null)), mockLogger.Object);
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "hello") };

        // Act — should NOT throw; should yield from AzureFoundry
        var chunks = new List<ChatResponseUpdate>();
        await foreach (var chunk in router.GetStreamingResponseAsync(messages))
            chunks.Add(chunk);

        // Assert
        Assert.Single(chunks);
        Assert.Equal("AzureFoundry response", chunks[0].Text);
        // Inner client should never be touched (failover found a working provider)
        mockInnerClient.Verify(
            c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// When FoundryLocal (primary) AND every provider in the failover chain all throw
    /// AggregateException (e.g. all local providers unavailable), the method must surface
    /// a controlled InvalidOperationException — NOT the raw AggregateException from
    /// System.ClientModel.
    /// </summary>
    [Fact]
    public async Task Streaming_ThrowsControlledInvalidOperationException_WhenAllProvidersAndFallbacksFail()
    {
        // Arrange — both primary and fallback throw the FoundryLocal-style AggregateException
        var primaryClient  = ClientStreamingThrowsAggregateException();
        var fallbackClient = ClientStreamingThrowsAggregateException();

        var mockStrategy = new Mock<IRoutingStrategy>();
        mockStrategy
            .Setup(s => s.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.FoundryLocal);

        var mockFailoverStrategy = new Mock<IFailoverStrategy>();
        mockFailoverStrategy
            .Setup(f => f.GetFailoverChainAsync(RouteDestination.FoundryLocal))
            .Returns(new List<RouteDestination> { RouteDestination.AzureFoundry });

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>("FoundryLocal", primaryClient.Object);
        services.AddKeyedSingleton<IChatClient>("AzureFoundry", fallbackClient.Object);
        var serviceProvider = services.BuildServiceProvider();

        var mockInnerClient = new Mock<IChatClient>();
        var mockLogger = new Mock<ILogger<LlmRoutingChatClient>>();
        var router = new LlmRoutingChatClient(
            mockInnerClient.Object, serviceProvider, mockStrategy.Object,
            mockFailoverStrategy.Object, CreateAvailabilityRegistry(("FoundryLocal", true, null), ("AzureFoundry", true, null)), mockLogger.Object);
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "hello") };

        // Act & Assert — must be a clean InvalidOperationException, not a raw AggregateException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in router.GetStreamingResponseAsync(messages)) { }
        });

        Assert.Contains("All providers in failover chain failed", ex.Message);
        Assert.IsNotType<AggregateException>(ex);
    }

    [Fact]
    public async Task SkipsUnavailablePrimaryProvider_AndUsesAvailableFallback()
    {
        var mockFoundryLocalClient = new Mock<IChatClient>();
        var mockAzureFoundryClient = new Mock<IChatClient>();
        mockAzureFoundryClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, "Azure fallback") }));

        var mockInnerClient = new Mock<IChatClient>();
        var mockLogger = new Mock<ILogger<LlmRoutingChatClient>>();
        var mockStrategy = new Mock<IRoutingStrategy>();
        mockStrategy
            .Setup(s => s.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.FoundryLocal);

        var mockFailoverStrategy = new Mock<IFailoverStrategy>();
        mockFailoverStrategy.Setup(f => f.GetFailoverChainAsync(RouteDestination.FoundryLocal))
            .Returns(new List<RouteDestination> { RouteDestination.AzureFoundry });

        var services = new ServiceCollection();
        services.AddKeyedSingleton<IChatClient>("FoundryLocal", mockFoundryLocalClient.Object);
        services.AddKeyedSingleton<IChatClient>("AzureFoundry", mockAzureFoundryClient.Object);
        var serviceProvider = services.BuildServiceProvider();

        var router = new LlmRoutingChatClient(
            mockInnerClient.Object,
            serviceProvider,
            mockStrategy.Object,
            mockFailoverStrategy.Object,
            CreateAvailabilityRegistry(("FoundryLocal", false, "offline"), ("AzureFoundry", true, null)),
            mockLogger.Object);

        var result = await router.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("Azure fallback", result.Text);
        mockFoundryLocalClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
