using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Handler for POST /v1/completions endpoint.
/// Legacy text-only completions endpoint with streaming support.
/// </summary>
public static class CompletionsEndpoint
{
    /// <summary>Handle text completion requests</summary>
    public static async Task<IResult> HandleAsync(
        TextCompletionRequest req,
        IChatClient chatClient,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(req.Model))
            return Results.BadRequest(new
            {
                error = new
                {
                    message = "Missing required field: model",
                    type = "invalid_request_error",
                    code = "missing_field"
                }
            });

        if (string.IsNullOrWhiteSpace(req.Prompt))
            return Results.BadRequest(new
            {
                error = new
                {
                    message = "Missing required field: prompt",
                    type = "invalid_request_error",
                    code = "missing_field"
                }
            });

        // Convert text prompt to chat format
        var messages = new List<ChatMessage> { new(ChatRole.User, req.Prompt) };

        // Build ChatOptions from request
        var options = new ChatOptions
        {
            Temperature = req.Temperature,
            MaxOutputTokens = req.MaxTokens,
            TopP = req.TopP,
            FrequencyPenalty = req.FrequencyPenalty,
            PresencePenalty = req.PresencePenalty
        };

        if (req.Stream)
        {
            // Streaming response via SSE
            return await HandleStreamingAsync(httpContext, messages, options, req.Model, chatClient, ct);
        }
        else
        {
            // Non-streaming response
            return await HandleNonStreamingAsync(messages, options, req.Model, chatClient, ct);
        }
    }

    private static async Task<IResult> HandleStreamingAsync(
        HttpContext httpContext,
        List<ChatMessage> messages,
        ChatOptions options,
        string model,
        IChatClient chatClient,
        CancellationToken ct)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("Connection", "keep-alive");

        var id = $"cmpl-{Guid.NewGuid().ToString("N").Substring(0, 24)}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, ct))
            {
                var choice = new { text = update.Text, index = 0, finish_reason = (string?)null };
                var chunk = new { id, @object = "text_completion.chunk", created, model, choices = new[] { choice } };
                var json = JsonSerializer.Serialize(chunk);
                await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            }
        }
        finally
        {
            await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
        }

        return Results.Empty;
    }

    private static async Task<IResult> HandleNonStreamingAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        string model,
        IChatClient chatClient,
        CancellationToken ct)
    {
        try
        {
            var completion = await chatClient.CompleteAsync(messages, options, ct);

            var id = $"cmpl-{Guid.NewGuid().ToString("N").Substring(0, 24)}";
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var choices = new List<TextChoice>
            {
                new(Index: 0, Text: completion.Message.Text ?? "", FinishReason: "stop")
            };

            var result = new TextCompletionResponse(
                Id: id,
                Object: "text_completion",
                Created: created,
                Model: model,
                Choices: choices,
                Usage: null
            );

            return Results.Json(result);
        }
        catch (Exception)
        {
            return Results.StatusCode(500);
        }
    }
}
