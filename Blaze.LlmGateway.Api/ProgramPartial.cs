using System.Text.Json;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Extension methods for registering LiteLLM-compatible endpoints.
/// </summary>
public static class LiteLlmEndpoints
{
    public static void RegisterLiteLlmEndpoints(this WebApplication app)
    {
        // Chat completions endpoint
        app.MapPost("/v1/chat/completions", async (
            ChatCompletionRequest req,
            IChatClient chatClient,
            HttpContext httpContext,
            CancellationToken ct) =>
            await ChatCompletionsEndpoint.HandleAsync(req, chatClient, httpContext, ct))
        .WithName("ChatCompletions")
        .Produces(200);

        // Text completions endpoint
        app.MapPost("/v1/completions", async (
            TextCompletionRequest req,
            IChatClient chatClient,
            HttpContext httpContext,
            CancellationToken ct) =>
            await CompletionsEndpoint.HandleAsync(req, chatClient, httpContext, ct))
        .WithName("TextCompletions")
        .Produces(200);

        // Models endpoint
        app.MapGet("/v1/models", (IServiceProvider sp) =>
            ModelsEndpoint.Handle(sp))
        .WithName("ListModels")
        .Produces(200);
    }
}
