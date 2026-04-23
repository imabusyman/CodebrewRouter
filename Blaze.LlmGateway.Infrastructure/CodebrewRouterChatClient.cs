using System.Runtime.CompilerServices;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.TaskRouting;
using Blaze.LlmGateway.Infrastructure.TaskClassification;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Infrastructure;

/// <summary>
/// Task-aware virtual LLM router registered under the keyed DI key <c>"CodebrewRouter"</c>.
/// When a caller sends <c>model: "codebrewRouter"</c> the <see cref="ModelSelectionResolver"/>
/// resolves to this client, which:
/// <list type="number">
///   <item>Classifies the incoming conversation into a <see cref="TaskType"/>.</item>
///   <item>Looks up the ordered provider fallback chain from <see cref="CodebrewRouterOptions.FallbackRules"/>.</item>
///   <item>Tries each provider in sequence; on any exception the next is attempted.</item>
///   <item>Falls back to <see cref="DelegatingChatClient.InnerClient"/> (AzureFoundry) when all providers fail.</item>
/// </list>
/// </summary>
public sealed class CodebrewRouterChatClient(
    IChatClient innerClient,
    ITaskClassifier taskClassifier,
    IOptions<CodebrewRouterOptions> options,
    IServiceProvider serviceProvider,
    ILogger<CodebrewRouterChatClient> logger) : DelegatingChatClient(innerClient)
{
    private CodebrewRouterOptions Options => options.Value;

    // ── Non-streaming ─────────────────────────────────────────────────────────

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var (taskType, providers) = await ResolveAsync(messageList, cancellationToken);

        for (var i = 0; i < providers.Length; i++)
        {
            var key = providers[i];
            var client = serviceProvider.GetKeyedService<IChatClient>(key);
            if (client is null)
            {
                logger.LogDebug("⚠️ codebrewRouter provider '{Key}' not registered — skipping", key);
                continue;
            }

            logger.LogInformation("🎯 codebrewRouter trying {Key} (attempt {Attempt}/{Total}) for {TaskType}",
                key, i + 1, providers.Length, taskType);
            try
            {
                var response = await client.GetResponseAsync(messageList, options, cancellationToken);
                logger.LogInformation("✅ codebrewRouter succeeded with {Key} for {TaskType}", key, taskType);
                return response;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "⚠️ codebrewRouter provider {Key} failed: {Message}. Trying next.", key, ex.Message);
            }
        }

        logger.LogWarning("⚠️ codebrewRouter all providers exhausted for {TaskType} — using InnerClient", taskType);
        return await InnerClient.GetResponseAsync(messageList, options, cancellationToken);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messageList = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var (taskType, providers) = await ResolveAsync(messageList, cancellationToken);

        for (var i = 0; i < providers.Length; i++)
        {
            var key = providers[i];
            var client = serviceProvider.GetKeyedService<IChatClient>(key);
            if (client is null)
            {
                logger.LogDebug("⚠️ codebrewRouter provider '{Key}' not registered — skipping", key);
                continue;
            }

            logger.LogInformation("🎯 codebrewRouter streaming: trying {Key} (attempt {Attempt}/{Total}) for {TaskType}",
                key, i + 1, providers.Length, taskType);

            // TryGetFirstChunkAsync is a plain async method (not an iterator),
            // so try/catch inside it is fully legal. It peeks at the first chunk
            // to confirm the provider is reachable. If it fails, we try the next.
            var result = await TryGetFirstChunkAsync(client, messageList, options, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning("⚠️ codebrewRouter streaming provider {Key} failed before first chunk. Trying next.", key);
                continue;
            }

            logger.LogInformation("✅ codebrewRouter streaming succeeded with {Key} for {TaskType}", key, taskType);

            // Yield the first chunk that was already fetched to confirm connectivity.
            yield return result.FirstChunk;

            // Stream the rest. Note: yield is outside try/catch (required by C# spec).
            // Enumerator is guaranteed non-null when Success == true (see FirstChunkResult).
            var enumerator = result.Enumerator!;
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
                    logger.LogWarning(ex, "⚠️ codebrewRouter mid-stream failure from {Key} — ending stream", key);
                    streamFailed = true;
                }

                if (streamFailed || !hasMore)
                {
                    await enumerator.DisposeAsync();
                    yield break;
                }

                yield return enumerator.Current;
            }
        }

        // All providers failed — fall back to InnerClient stream.
        logger.LogWarning("⚠️ codebrewRouter all streaming providers exhausted for {TaskType} — using InnerClient", taskType);
        await foreach (var update in InnerClient.GetStreamingResponseAsync(messageList, options, cancellationToken))
            yield return update;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<(TaskType TaskType, string[] Providers)> ResolveAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var taskType = await taskClassifier.ClassifyAsync(messages, cancellationToken);
        logger.LogInformation("🧠 codebrewRouter classified task as {TaskType}", taskType);

        var typeKey = taskType.ToString();
        var providers =
            Options.FallbackRules.TryGetValue(typeKey, out var chain) && chain.Length > 0 ? chain
            : Options.FallbackRules.TryGetValue("General", out var general) ? general
            : [];

        return (taskType, providers);
    }

    /// <summary>
    /// Regular async method (NOT an iterator) that tries to obtain the first chunk of a
    /// streaming response. Because this is not an iterator, try/catch works normally.
    /// Returns a success flag, the first chunk, and the still-open enumerator so the
    /// caller can continue streaming without restarting.
    /// </summary>
    private static async Task<FirstChunkResult> TryGetFirstChunkAsync(
        IChatClient client,
        IList<ChatMessage> messages,
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

    private sealed record FirstChunkResult(
        bool Success,
        ChatResponseUpdate FirstChunk,
        IAsyncEnumerator<ChatResponseUpdate>? Enumerator)
    {
        public static readonly FirstChunkResult Failed = new(false, default!, null);
    }
}
