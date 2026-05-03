using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.Infrastructure;

/// <summary>
/// Wraps a chat client with automatic fallback to a secondary client on failure.
/// Used to gracefully degrade when primary provider (e.g., LM Studio) is unavailable.
/// </summary>
public sealed class FallbackChatClient(
    IChatClient primary,
    IChatClient fallback,
    ILogger<FallbackChatClient> logger) : DelegatingChatClient(primary)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("🔀 Trying primary provider");
            return await InnerClient.GetResponseAsync(chatMessages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "⚠️ Primary failed; trying fallback");
            try
            {
                return await fallback.GetResponseAsync(chatMessages, options, cancellationToken);
            }
            catch (Exception fallbackEx)
            {
                logger.LogError(fallbackEx, "❌ Both providers failed");
                throw;
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("🔀 Streaming: trying primary");
        return InnerClient.GetStreamingResponseAsync(chatMessages, options, cancellationToken);
    }
}
