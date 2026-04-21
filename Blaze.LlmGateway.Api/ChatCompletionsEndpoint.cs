using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Blaze.LlmGateway.Api;

/// <summary>
/// Handler for POST /v1/chat/completions endpoint.
/// OpenAI-compatible chat completion endpoint with streaming support.
/// </summary>
public static class ChatCompletionsEndpoint
{
    /// <summary>Handle chat completion requests</summary>
    public static async Task<IResult> HandleAsync(
        ChatCompletionRequest req,
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

        if (req.Messages == null || req.Messages.Count == 0)
            return Results.BadRequest(new
            {
                error = new
                {
                    message = "Missing required field: messages",
                    type = "invalid_request_error",
                    code = "missing_field"
                }
            });

        // Convert request DTOs to ChatMessage list
        var messages = new List<ChatMessage>();
        foreach (var msg in req.Messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
            {
                "system" => ChatRole.System,
                "assistant" => ChatRole.Assistant,
                _ => ChatRole.User
            };
            messages.Add(new ChatMessage(role, msg.Content));
        }

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

        var id = $"chatcmpl-{Guid.NewGuid().ToString("N").Substring(0, 24)}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, ct))
            {
                var choice = new { index = 0, delta = new { content = update.Text }, finish_reason = (string?)null };
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

            var id = $"chatcmpl-{Guid.NewGuid().ToString("N").Substring(0, 24)}";
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var choices = new List<Choice>
            {
                new(
                    Index: 0,
                    Message: new ChatMessageDto(Role: "assistant", Content: completion.Message.Text ?? ""),
                    Delta: null,
                    FinishReason: "stop"
                )
            };

            var result = new ChatCompletionResponse(
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
