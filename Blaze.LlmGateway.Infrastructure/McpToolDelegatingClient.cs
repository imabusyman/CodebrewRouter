using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Blaze.LlmGateway.Infrastructure;

public class McpToolDelegatingClient(
    IChatClient innerClient,
    McpConnectionManager mcpConnectionManager,
    ILogger<McpToolDelegatingClient> logger) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        options = AppendMcpTools(options);
        return await base.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = AppendMcpTools(options);
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            yield return update;
        }
    }

    private ChatOptions AppendMcpTools(ChatOptions? options)
    {
        options ??= new ChatOptions();
        options.Tools ??= [];

        var tools = mcpConnectionManager.GetAllTools().ToList();
        if (tools.Count == 0) return options;

        var aiTools = options.Tools.ToList();
        foreach (var tool in tools)
        {
            logger.LogDebug("Appending MCP tool: {ToolName}", (tool as AIFunction)?.Name ?? tool.GetType().Name);
            aiTools.Add(tool);
        }

        options.Tools = aiTools;
        return options;
    }
}
