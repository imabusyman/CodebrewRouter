using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core;
using Microsoft.Extensions.AI;

namespace Blaze.LlmGateway.Infrastructure.RoutingStrategies;

public interface IRoutingStrategy
{
    Task<RouteDestination> ResolveAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default);
}
