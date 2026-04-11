using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Blaze.LlmGateway.Infrastructure;

public class McpConnectionConfig
{
    public string Id { get; set; } = "";
    public string TransportType { get; set; } = "";
    public string? Command { get; set; }
    public string[]? Arguments { get; set; }
    public string? Url { get; set; }
}

public class McpConnectionManager : IHostedService
{
    private readonly IEnumerable<McpConnectionConfig> _configs;
    private readonly ILogger<McpConnectionManager> _logger;
    private readonly ConcurrentDictionary<string, McpClient> _clients = new();
    private readonly ConcurrentDictionary<string, List<AITool>> _cachedTools = new();

    public McpConnectionManager(IEnumerable<McpConnectionConfig> configs, ILogger<McpConnectionManager> logger)
    {
        _configs = configs;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var config in _configs)
        {
            try
            {
                _logger.LogInformation("Connecting to MCP server: {McpId}", config.Id);

                IClientTransport? transport = null;

                if (config.TransportType == "Stdio" && !string.IsNullOrEmpty(config.Command))
                {
                    transport = new StdioClientTransport(new()
                    {
                        Command = config.Command,
                        Arguments = config.Arguments ?? []
                    });
                }
                else if (config.TransportType == "Http" && !string.IsNullOrEmpty(config.Url))
                {
                    transport = new HttpClientTransport(new()
                    {
                        Endpoint = new Uri(config.Url)
                    });
                }

                if (transport != null)
                {
                    var client = await McpClient.CreateAsync(transport);
                    _clients[config.Id] = client;

                    var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                    _cachedTools[config.Id] = tools.Cast<AITool>().ToList();

                    _logger.LogInformation("Connected to {McpId} with {ToolCount} tools", config.Id, _cachedTools[config.Id].Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server: {McpId}", config.Id);
            }
        }
    }

    public IEnumerable<AITool> GetAllTools()
    {
        return _cachedTools.Values.SelectMany(x => x);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var client in _clients.Values)
        {
            await client.DisposeAsync();
        }
        _clients.Clear();
        _cachedTools.Clear();
    }
}
