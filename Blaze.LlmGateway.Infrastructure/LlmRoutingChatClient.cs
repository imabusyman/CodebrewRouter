using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.Infrastructure;

public class LlmRoutingChatClient : DelegatingChatClient
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRoutingStrategy _routingStrategy;
    private readonly IFailoverStrategy _failoverStrategy;
    private readonly IModelAvailabilityRegistry _availabilityRegistry;
    private readonly ILogger<LlmRoutingChatClient> _logger;

    public LlmRoutingChatClient(
        IChatClient innerClient,
        IServiceProvider serviceProvider,
        IRoutingStrategy routingStrategy,
        IFailoverStrategy failoverStrategy,
        IModelAvailabilityRegistry availabilityRegistry,
        ILogger<LlmRoutingChatClient> logger) : base(innerClient)
    {
        _serviceProvider = serviceProvider;
        _routingStrategy = routingStrategy;
        _failoverStrategy = failoverStrategy;
        _availabilityRegistry = availabilityRegistry;
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

        // Use first-chunk probe to detect failures early; if it fails, try failover chain
        var result = await TryGetFirstChunkAsync(targetClient, chatMessages, options, cancellationToken);
        if (!result.Success)
        {
            _logger.LogWarning("⚠️ Primary provider failed before first chunk — attempting failover chain...");
            
            // Yield all chunks from failover chain
            await foreach (var update in TryFailoverStreamingAsync(chatMessages, options, cancellationToken))
                yield return update;
            
            yield break;
        }

        // Yield the first chunk that was already fetched
        yield return result.FirstChunk;
        _logger.LogDebug("  ├─ First chunk received");

        // Stream the rest
        var enumerator = result.Enumerator!;
        var chunkCount = 1;
        while (true)
        {
            bool hasMore = false;
            bool streamFailed = false;
            try
            {
                hasMore = await enumerator.MoveNextAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Mid-stream failure from primary provider — ending stream");
                streamFailed = true;
            }

            if (streamFailed || !hasMore)
            {
                await enumerator.DisposeAsync();
                _logger.LogInformation("✅ Streaming complete - {ChunkCount} updates", chunkCount);
                yield break;
            }

            chunkCount++;
            yield return enumerator.Current;
        }
    }

    private async Task<IChatClient> ResolveTargetClientAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
    {
        _logger.LogDebug("🔀 Routing strategy resolving...");
        var destination = await _routingStrategy.ResolveAsync(messages, cancellationToken);
        _logger.LogDebug("  ├─ Routing strategy decided: {Destination}", destination);

        if (!_availabilityRegistry.IsProviderAvailable(destination.ToString()))
        {
            _logger.LogWarning("❌ Destination '{Destination}' is currently unavailable. Deferring to failover chain.", destination);
            return new UnavailableChatClient($"Provider '{destination}' is currently unavailable.");
        }
        
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
                if (!_availabilityRegistry.IsProviderAvailable(fallback.ToString()))
                {
                    _logger.LogDebug("  ├─ Failover provider {Fallback} is marked unavailable; skipping", fallback);
                    continue;
                }

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
            if (!_availabilityRegistry.IsProviderAvailable(fallback.ToString()))
            {
                _logger.LogDebug("  ├─ Failover provider {Fallback} is marked unavailable; skipping", fallback);
                continue;
            }

            _logger.LogInformation("🔄 Trying failover provider (streaming): {Fallback}", fallback);
            var fallbackClient = _serviceProvider.GetKeyedService<IChatClient>(fallback.ToString());
            if (fallbackClient is null)
            {
                _logger.LogDebug("  ├─ Failover provider {Fallback} not registered; skipping", fallback);
                continue;
            }

            // First-chunk probe: catches connection-refused / retry-exhausted failures
            // (e.g. System.ClientModel AggregateException from FoundryLocal at 127.0.0.1)
            // before we commit to this provider. Without this probe, a raw AggregateException
            // propagates through the async-iterator chain to the caller.
            var probe = await TryGetFirstChunkAsync(fallbackClient, chatMessages, options, cancellationToken);
            if (!probe.Success)
            {
                _logger.LogWarning("  ├─ Failover provider {Fallback} failed before first chunk; trying next", fallback);
                continue;
            }

            _logger.LogInformation("✅ Failover streaming: first chunk received from {Fallback}", fallback);
            yield return probe.FirstChunk;

            var enumerator = probe.Enumerator!;
            int chunkCount = 1;
            while (true)
            {
                bool hasMore = false;
                bool midStreamFailed = false;
                try
                {
                    hasMore = await enumerator.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "  ├─ Failover provider {Fallback} failed mid-stream after {ChunkCount} chunks; ending", fallback, chunkCount);
                    midStreamFailed = true;
                }

                if (midStreamFailed || !hasMore)
                {
                    await enumerator.DisposeAsync();
                    _logger.LogInformation("✅ Failover streaming complete with {Fallback} ({ChunkCount} chunks)", fallback, chunkCount);
                    yield break;
                }

                chunkCount++;
                yield return enumerator.Current;
            }
        }

        _logger.LogError("❌ All failover providers exhausted (streaming); throwing error");
        throw new InvalidOperationException("All providers in failover chain failed during streaming");
    }

    /// <summary>
    /// Attempts to get the first chunk from a provider to detect early failures.
    /// This is a plain async method (not an iterator), so try/catch is fully legal.
    /// </summary>
    private static async Task<FirstChunkResult> TryGetFirstChunkAsync(
        IChatClient client,
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken ct)
    {
        var enumerator = client.GetStreamingResponseAsync(messages, options, ct)
                                .GetAsyncEnumerator(ct);
        try
        {
            if (!await enumerator.MoveNextAsync())
            {
                await enumerator.DisposeAsync();
                return FirstChunkResult.Failed;
            }

            return new FirstChunkResult(true, enumerator.Current, enumerator);
        }
        catch
        {
            await enumerator.DisposeAsync();
            return FirstChunkResult.Failed;
        }
    }
}

/// <summary>Encapsulates the result of probing for the first chunk from a streaming response.</summary>
internal record FirstChunkResult(bool Success, ChatResponseUpdate FirstChunk = default!, IAsyncEnumerator<ChatResponseUpdate>? Enumerator = null)
{
    public static readonly FirstChunkResult Failed = new(false);
}
