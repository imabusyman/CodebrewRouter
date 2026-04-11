using System.Text.Json;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmGatewayOptions>(
    builder.Configuration.GetSection(LlmGatewayOptions.SectionName));

// MCP connection manager (hosted service + singleton accessor)
builder.Services.AddSingleton<IEnumerable<McpConnectionConfig>>([
    new McpConnectionConfig
    {
        Id = "microsoft-learn",
        TransportType = "Stdio",
        Command = "npx",
        Arguments = ["-y", "@microsoft/mcp-server-microsoft-learn"]
    }
]);
builder.Services.AddHostedService<McpConnectionManager>();
builder.Services.AddSingleton(sp =>
    sp.GetServices<IHostedService>().OfType<McpConnectionManager>().First());

// Keyed provider clients + routing/MCP pipeline
builder.Services.AddLlmProviders();
builder.Services.AddLlmInfrastructure();

var app = builder.Build();

app.MapPost("/v1/chat/completions", async (HttpRequest request, IChatClient chatClient) =>
{
    using var doc = await JsonSerializer.DeserializeAsync<JsonDocument>(request.Body);
    var messages = new List<ChatMessage>();

    if (doc?.RootElement.TryGetProperty("messages", out var msgsEl) == true)
    {
        foreach (var msg in msgsEl.EnumerateArray())
        {
            var roleStr = msg.GetProperty("role").GetString() ?? "user";
            var content = msg.GetProperty("content").GetString() ?? "";
            var role = roleStr.ToLowerInvariant() switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            };
            messages.Add(new ChatMessage(role, content));
        }
    }

    request.HttpContext.Response.ContentType = "text/event-stream";
    await foreach (var update in chatClient.GetStreamingResponseAsync(messages))
    {
        var chunk = new { choices = new[] { new { delta = new { content = update.Text } } } };
        await request.HttpContext.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk)}\n\n");
    }
    await request.HttpContext.Response.WriteAsync("data: [DONE]\n\n");
});

app.Run();
