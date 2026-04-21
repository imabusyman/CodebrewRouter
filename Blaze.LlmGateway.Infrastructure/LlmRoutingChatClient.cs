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

public class LlmRoutingChatClient : DelegatingChatClient
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRoutingStrategy _routingStrategy;
    private readonly ILogger<LlmRoutingChatClient> _logger;

    public LlmRoutingChatClient(
        IChatClient innerClient,
        IServiceProvider serviceProvider,
        IRoutingStrategy routingStrategy,
        ILogger<LlmRoutingChatClient> logger) : base(innerClient)
    {
        _serviceProvider = serviceProvider;
        _routingStrategy = routingStrategy;
        _logger = logger;
    }
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🔀 ResolveTargetClient: Getting routing decision...");
        var targetClient = await ResolveTargetClientAsync(chatMessages, cancellationToken);
        _logger.LogInformation("🎯 Routing request to: {TargetClient}", targetClient.GetType().Name);
        
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await targetClient.GetResponseAsync(chatMessages, options, cancellationToken);
        sw.Stop();
        _logger.LogInformation("✅ Provider responded in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🔀 ResolveTargetClient: Getting routing decision...");
        var targetClient = await ResolveTargetClientAsync(chatMessages, cancellationToken);
        _logger.LogInformation("🎯 Routing streaming request to: {TargetClient}", targetClient.GetType().Name);

        var chunkCount = 0;
        await foreach (var update in targetClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            chunkCount++;
            if (chunkCount == 1)
                _logger.LogDebug("  ├─ First chunk received from provider");
            yield return update;
        }
        _logger.LogInformation("✅ Streaming complete - {ChunkCount} updates from provider", chunkCount);
    }

    private async Task<IChatClient> ResolveTargetClientAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        _logger.LogDebug("🔀 Routing strategy resolving...");
        var destination = await _routingStrategy.ResolveAsync(messages, cancellationToken);
        _logger.LogDebug("  ├─ Routing strategy decided: {Destination}", destination);
        
        var client = _serviceProvider.GetKeyedService<IChatClient>(destination.ToString());

        if (client is null)
        {
            _logger.LogWarning("❌ No client registered for destination '{Destination}'. Using fallback to InnerClient.", destination);
            return InnerClient;
        }

        _logger.LogDebug("  └─ Found registered client for {Destination}", destination);
        return client;
    }
}

