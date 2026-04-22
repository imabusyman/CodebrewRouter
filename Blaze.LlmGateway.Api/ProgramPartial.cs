using System.Text.Json;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
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
            IModelSelectionResolver modelSelectionResolver,
            HttpContext httpContext,
            CancellationToken ct) =>
            await ChatCompletionsEndpoint.HandleAsync(req, chatClient, modelSelectionResolver, httpContext, ct))
        .WithName("ChatCompletions")
        .Produces(200);

        // Text completions endpoint
        app.MapPost("/v1/completions", async (
            TextCompletionRequest req,
            IChatClient chatClient,
            IModelSelectionResolver modelSelectionResolver,
            HttpContext httpContext,
            CancellationToken ct) =>
            await CompletionsEndpoint.HandleAsync(req, chatClient, modelSelectionResolver, httpContext, ct))
        .WithName("TextCompletions")
        .Produces(200);

        // Models endpoint
        app.MapGet("/v1/models", (IModelCatalog modelCatalog, CancellationToken ct) =>
            ModelsEndpoint.HandleAsync(modelCatalog, ct))
        .WithName("ListModels")
        .Produces(200);
    }
}
