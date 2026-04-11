using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.Infrastructure.RoutingStrategies;

/// <summary>
/// Keyword-based routing strategy used as a fallback when the meta-router is unavailable.
/// Routes based on keywords found in the last user message.
/// </summary>
public class KeywordRoutingStrategy(
    ILogger<KeywordRoutingStrategy> logger,
    RouteDestination defaultDestination = RouteDestination.AzureFoundry) : IRoutingStrategy
{
    public Task<RouteDestination> ResolveAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.ToLowerInvariant() ?? "";

        var destination = lastUserMessage switch
        {
            _ when lastUserMessage.Contains("ollama backup") || lastUserMessage.Contains("ollamabackup") => RouteDestination.OllamaBackup,
            _ when lastUserMessage.Contains("ollama") => RouteDestination.Ollama,
            _ when lastUserMessage.Contains("copilot") || lastUserMessage.Contains("github") => RouteDestination.GithubCopilot,
            _ when lastUserMessage.Contains("gemini") => RouteDestination.Gemini,
            _ when lastUserMessage.Contains("openrouter") || lastUserMessage.Contains("qwen") => RouteDestination.OpenRouter,
            _ when lastUserMessage.Contains("azure") => RouteDestination.AzureFoundry,
            _ => defaultDestination
        };

        logger.LogDebug("Keyword routing selected destination: {Destination}", destination);
        return Task.FromResult(destination);
    }
}
