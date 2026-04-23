using Blaze.LlmGateway.Core.TaskRouting;
using Microsoft.Extensions.AI;

namespace Blaze.LlmGateway.Infrastructure.TaskClassification;

/// <summary>
/// Classifies an incoming chat conversation into a <see cref="TaskType"/> so that
/// <c>CodebrewRouterChatClient</c> can select the best provider fallback chain.
/// </summary>
public interface ITaskClassifier
{
    /// <summary>
    /// Inspect <paramref name="messages"/> and return the most likely <see cref="TaskType"/>.
    /// Implementations must never throw — return <see cref="TaskType.General"/> as the safe default.
    /// </summary>
    Task<TaskType> ClassifyAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
}
