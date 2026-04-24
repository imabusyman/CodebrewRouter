using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Infrastructure;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;

namespace Blaze.LlmGateway.Tests;

public class LlmRoutingChatClientTests
{
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

        var router = new LlmRoutingChatClient(mockInnerClient.Object, serviceProvider, mockStrategy.Object, mockFailoverStrategy.Object, mockLogger.Object);
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

        var router = new LlmRoutingChatClient(mockInnerClient.Object, serviceProvider, mockStrategy.Object, mockFailoverStrategy.Object, mockLogger.Object);
        var messages = new List<ChatMessage> { new ChatMessage(ChatRole.User, "What is 2+2?") };

        // Act
        var result = await router.GetResponseAsync(messages);

        // Assert
        Assert.Equal("Fallback Response", result.Text);
        mockInnerClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
