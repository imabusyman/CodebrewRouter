using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.Infrastructure;

public class LlmRoutingChatClient(
    IChatClient innerClient,
    IServiceProvider serviceProvider,
    IRoutingStrategy routingStrategy,
    ILogger<LlmRoutingChatClient> logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var targetClient = await ResolveTargetClientAsync(chatMessages, cancellationToken);
        logger.LogInformation("Routing request to {TargetClient}", targetClient.GetType().Name);
        return await targetClient.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var targetClient = await ResolveTargetClientAsync(chatMessages, cancellationToken);
        logger.LogInformation("Routing streaming request to {TargetClient}", targetClient.GetType().Name);

        await foreach (var update in targetClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private async Task<IChatClient> ResolveTargetClientAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        var destination = await routingStrategy.ResolveAsync(messages, cancellationToken);
        var client = serviceProvider.GetKeyedService<IChatClient>(destination.ToString());

        if (client is null)
        {
            logger.LogWarning("No client registered for destination '{Destination}'. Using fallback.", destination);
            return innerClient;
        }

        return client;
    }
}

