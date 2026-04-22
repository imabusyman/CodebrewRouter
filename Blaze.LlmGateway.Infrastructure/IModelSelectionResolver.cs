using Microsoft.Extensions.AI;

namespace Blaze.LlmGateway.Infrastructure;

public interface IModelSelectionResolver
{
    Task<IChatClient?> ResolveAsync(string modelId, CancellationToken cancellationToken = default);
}
