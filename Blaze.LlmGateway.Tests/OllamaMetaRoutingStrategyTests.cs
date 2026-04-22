using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

public class OllamaMetaRoutingStrategyTests
{
    private static OllamaMetaRoutingStrategy CreateStrategy(
        Mock<IChatClient> routerClient,
        Mock<IRoutingStrategy> fallback,
        Mock<ILogger<OllamaMetaRoutingStrategy>>? logger = null)
    {
        logger ??= new Mock<ILogger<OllamaMetaRoutingStrategy>>();
        return new OllamaMetaRoutingStrategy(routerClient.Object, fallback.Object, logger.Object);
    }

    private static List<ChatMessage> UserMessages(string text) =>
        [new ChatMessage(ChatRole.User, text)];

    [Theory]
    [InlineData("AzureFoundry", RouteDestination.AzureFoundry)]
    [InlineData("Gemini", RouteDestination.Gemini)]
    [InlineData("OpenRouter", RouteDestination.OpenRouter)]
    [InlineData("GithubCopilot", RouteDestination.GithubCopilot)]
    [InlineData("OllamaLocal", RouteDestination.OllamaLocal)]
    public async Task ReturnsCorrectDestination_WhenRouterReturnsExactName(string routerResponse, RouteDestination expected)
    {
        var mockRouter = new Mock<IChatClient>();
        mockRouter
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, routerResponse)]));

        var fallback = new Mock<IRoutingStrategy>();
        var strategy = CreateStrategy(mockRouter, fallback);

        var result = await strategy.ResolveAsync(UserMessages("Some prompt"));

        Assert.Equal(expected, result);
        fallback.Verify(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReturnsCorrectDestination_WhenRouterResponseIsCaseInsensitive()
    {
        var mockRouter = new Mock<IChatClient>();
        mockRouter
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "gemini")]));

        var fallback = new Mock<IRoutingStrategy>();
        var strategy = CreateStrategy(mockRouter, fallback);

        var result = await strategy.ResolveAsync(UserMessages("Search google"));

        Assert.Equal(RouteDestination.Gemini, result);
    }

    [Fact]
    public async Task UsesFallback_WhenRouterReturnsUnrecognizedText()
    {
        var mockRouter = new Mock<IChatClient>();
        mockRouter
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "I don't know which one to pick")]));

        var fallback = new Mock<IRoutingStrategy>();
        fallback
            .Setup(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.OllamaLocal);

        var strategy = CreateStrategy(mockRouter, fallback);

        var result = await strategy.ResolveAsync(UserMessages("Hello"));

        Assert.Equal(RouteDestination.OllamaLocal, result);
        fallback.Verify(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UsesFallback_WhenRouterThrowsException()
    {
        var mockRouter = new Mock<IChatClient>();
        mockRouter
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection refused"));

        var fallback = new Mock<IRoutingStrategy>();
        fallback
            .Setup(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.AzureFoundry);

        var strategy = CreateStrategy(mockRouter, fallback);

        var result = await strategy.ResolveAsync(UserMessages("Hello"));

        Assert.Equal(RouteDestination.AzureFoundry, result);
        fallback.Verify(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UsesFallback_WhenMessagesAreEmpty()
    {
        var mockRouter = new Mock<IChatClient>();
        var fallback = new Mock<IRoutingStrategy>();
        fallback
            .Setup(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RouteDestination.OllamaLocal);

        var strategy = CreateStrategy(mockRouter, fallback);

        var result = await strategy.ResolveAsync([]);

        Assert.Equal(RouteDestination.OllamaLocal, result);
        mockRouter.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PartialMatch_WhenRouterResponseContainsDestinationName()
    {
        var mockRouter = new Mock<IChatClient>();
        mockRouter
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "I think you should use OpenRouter for this.")]));

        var fallback = new Mock<IRoutingStrategy>();
        var strategy = CreateStrategy(mockRouter, fallback);

        var result = await strategy.ResolveAsync(UserMessages("Write a poem"));

        Assert.Equal(RouteDestination.OpenRouter, result);
        fallback.Verify(f => f.ResolveAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
