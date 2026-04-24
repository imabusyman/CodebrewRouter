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
    private readonly IFailoverStrategy _failoverStrategy;
    private readonly ILogger<LlmRoutingChatClient> _logger;

    public LlmRoutingChatClient(
        IChatClient innerClient,
        IServiceProvider serviceProvider,
        IRoutingStrategy routingStrategy,
        IFailoverStrategy failoverStrategy,
        ILogger<LlmRoutingChatClient> logger) : base(innerClient)
    {
        _serviceProvider = serviceProvider;
        _routingStrategy = routingStrategy;
        _failoverStrategy = failoverStrategy;
        _logger = logger;
    }
    
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("🔀 ResolveTargetClient: Getting routing decision...");
        var targetClient = await ResolveTargetClientAsync(chatMessages, cancellationToken);
        _logger.LogInformation("🎯 Routing request to: {TargetClient}", targetClient.GetType().Name);
        
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var response = await targetClient.GetResponseAsync(chatMessages, options, cancellationToken);
            sw.Stop();
            _logger.LogInformation("✅ Provider responded in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Provider failed; attempting failover...");
            return await TryFailoverAsync(chatMessages, options, cancellationToken);
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return GetStreamingResponseAsyncImpl(chatMessages, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsyncImpl(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogDebug("🔀 ResolveTargetClient: Getting routing decision...");
        var targetClient = await ResolveTargetClientAsync(chatMessages, cancellationToken);
        _logger.LogInformation("🎯 Routing streaming request to: {TargetClient}", targetClient.GetType().Name);

        int chunkCount = 0;
        var responseStream = targetClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken);

        await foreach (var update in responseStream)
        {
            chunkCount++;
            if (chunkCount == 1)
                _logger.LogDebug("  ├─ First chunk received");
            yield return update;
        }

        _logger.LogInformation("✅ Streaming complete - {ChunkCount} updates", chunkCount);
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

    private async Task<ChatResponse> TryFailoverAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options, CancellationToken cancellationToken)
    {
        var destination = await _routingStrategy.ResolveAsync(chatMessages, cancellationToken);
        var fallbackChain = _failoverStrategy.GetFailoverChainAsync(destination);

        foreach (var fallback in fallbackChain)
        {
            try
            {
                _logger.LogInformation("🔄 Trying failover provider: {Fallback}", fallback);
                var fallbackClient = _serviceProvider.GetKeyedService<IChatClient>(fallback.ToString());
                if (fallbackClient is null)
                {
                    _logger.LogDebug("  ├─ Failover provider {Fallback} not registered; skipping", fallback);
                    continue;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await fallbackClient.GetResponseAsync(chatMessages, options, cancellationToken);
                sw.Stop();
                _logger.LogInformation("✅ Failover succeeded with {Fallback} in {ElapsedMs}ms", fallback, sw.ElapsedMilliseconds);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "  ├─ Failover provider {Fallback} also failed", fallback);
            }
        }

        _logger.LogError("❌ All failover providers exhausted; returning error");
        throw new InvalidOperationException("All providers in failover chain failed");
    }

    private async IAsyncEnumerable<ChatResponseUpdate> TryFailoverStreamingAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var destination = await _routingStrategy.ResolveAsync(chatMessages, cancellationToken);
        var fallbackChain = _failoverStrategy.GetFailoverChainAsync(destination);

        foreach (var fallback in fallbackChain)
        {
            _logger.LogInformation("🔄 Trying failover provider (streaming): {Fallback}", fallback);
            var fallbackClient = _serviceProvider.GetKeyedService<IChatClient>(fallback.ToString());
            if (fallbackClient is null)
            {
                _logger.LogDebug("  ├─ Failover provider {Fallback} not registered; skipping", fallback);
                continue;
            }

            int chunkCount = 0;
            var responseStream = fallbackClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken);

            await foreach (var update in responseStream)
            {
                chunkCount++;
                yield return update;
            }

            _logger.LogInformation("✅ Failover streaming succeeded with {Fallback} ({ChunkCount} chunks)", fallback, chunkCount);
            yield break;
        }

        _logger.LogError("❌ All failover providers exhausted (streaming); throwing error");
        throw new InvalidOperationException("All providers in failover chain failed during streaming");
    }
}
