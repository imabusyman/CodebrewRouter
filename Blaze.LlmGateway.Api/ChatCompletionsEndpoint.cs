using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Blaze.LlmGateway.Infrastructure;

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
        IModelSelectionResolver modelSelectionResolver,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = httpContext.RequestServices.GetService(typeof(ILogger<ChatCompletionRequest>)) as ILogger<ChatCompletionRequest>;
        
        logger?.LogInformation("📨 Chat completion request received - Model: {Model}, Stream: {Stream}, Messages: {MessageCount}", 
            req.Model, req.Stream, req.Messages?.Count ?? 0);

        // Validate required fields
        if (string.IsNullOrWhiteSpace(req.Model))
        {
            logger?.LogWarning("❌ Validation failed: Missing model field");
            return Results.BadRequest(new
            {
                error = new
                {
                    message = "Missing required field: model",
                    type = "invalid_request_error",
                    code = "missing_field"
                }
            });
        }

        if (req.Messages == null || req.Messages.Count == 0)
        {
            logger?.LogWarning("❌ Validation failed: Missing or empty messages field");
            return Results.BadRequest(new
            {
                error = new
                {
                    message = "Missing required field: messages",
                    type = "invalid_request_error",
                    code = "missing_field"
                }
            });
        }

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
            logger?.LogDebug("  ├─ Message added: {Role} - {ContentPreview}", 
                role, msg.Content.Substring(0, Math.Min(50, msg.Content.Length)));
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
        logger?.LogDebug("  ├─ ChatOptions: Temp={Temperature}, MaxTokens={MaxTokens}, TopP={TopP}", 
            req.Temperature, req.MaxTokens, req.TopP);

        if (req.Stream)
        {
            logger?.LogInformation("  └─ Using STREAMING mode");
            // Streaming response via SSE
            return await HandleStreamingAsync(httpContext, messages, options, req.Model, req.Tools, chatClient, modelSelectionResolver, logger, ct);
        }
        else
        {
            logger?.LogInformation("  └─ Using NON-STREAMING mode");
            // Non-streaming response
            return await HandleNonStreamingAsync(messages, options, req.Model, req.Tools, chatClient, modelSelectionResolver, logger, ct);
        }
    }

    private static async Task<IResult> HandleStreamingAsync(
        HttpContext httpContext,
        List<ChatMessage> messages,
        ChatOptions options,
        string model,
        IList<Tool>? tools,
        IChatClient chatClient,
        IModelSelectionResolver modelSelectionResolver,
        ILogger<ChatCompletionRequest>? logger,
        CancellationToken ct)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.Append("Cache-Control", "no-cache");
        httpContext.Response.Headers.Append("Connection", "keep-alive");
        // Disable proxy/nginx buffering so each SSE chunk reaches the client immediately.
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");

        var id = $"chatcmpl-{Guid.NewGuid().ToString("N").Substring(0, 24)}";
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        logger?.LogInformation("⏳ Starting streaming response - ID: {RequestId}", id);

        try
        {
            // Add tools to options if provided
            if (tools != null && tools.Count > 0)
            {
                logger?.LogDebug("  ├─ {ToolCount} tools requested (not yet translated to MEAI functions)", tools.Count);
                // TODO Phase 1.8: Translate Tool objects to AIFunction declarations via AIFunctionFactory.Create
                // For now, we parse them to log but don't yet execute tool-calling flows.
                // The FunctionInvokingChatClient won't have actual tool definitions, but the parse is validated.
            }

            var selectedClient = await ResolveClientAsync(model, chatClient, modelSelectionResolver, logger, ct);
            var chunkCount = 0;
            var firstChunkSent = false;

            await foreach (var update in selectedClient.GetStreamingResponseAsync(messages, options, ct))
            {
                chunkCount++;
                
                // Emit first chunk with role
                if (!firstChunkSent)
                {
                    var firstChoice = new { index = 0, delta = new { role = "assistant", content = "" }, finish_reason = (string?)null };
                    var firstChunk = new { id, @object = "chat.completion.chunk", created, model, choices = new[] { firstChoice } };
                    var json = JsonSerializer.Serialize(firstChunk);
                    await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                    firstChunkSent = true;
                    logger?.LogDebug("  ├─ First chunk with role sent");
                }

                // Emit content chunk
                var choice = new { index = 0, delta = new { content = update.Text }, finish_reason = (string?)null };
                var chunk = new { id, @object = "chat.completion.chunk", created, model, choices = new[] { choice } };
                var contentJson = JsonSerializer.Serialize(chunk);
                await httpContext.Response.WriteAsync($"data: {contentJson}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
                
                if (chunkCount % 10 == 0)
                    logger?.LogDebug("  ├─ Streamed {ChunkCount} chunks so far", chunkCount);
            }

            // Emit final chunk with finish_reason
            var finalChoice = new { index = 0, delta = new { }, finish_reason = "stop" };
            var finalChunk = new { id, @object = "chat.completion.chunk", created, model, choices = new[] { finalChoice } };
            var finalJson = JsonSerializer.Serialize(finalChunk);
            await httpContext.Response.WriteAsync($"data: {finalJson}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
            
            logger?.LogInformation("✅ Stream completed - Total chunks: {ChunkCount}", chunkCount);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "❌ Streaming error after {ChunkCount} chunks", 
                httpContext.Response.HasStarted ? "headers sent" : "headers not sent");
        }
        finally
        {
            await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
            logger?.LogDebug("  └─ [DONE] marker sent");
        }

        return Results.Empty;
    }

    private static async Task<IResult> HandleNonStreamingAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        string model,
        IList<Tool>? tools,
        IChatClient chatClient,
        IModelSelectionResolver modelSelectionResolver,
        ILogger<ChatCompletionRequest>? logger,
        CancellationToken ct)
    {
        try
        {
            // Add tools to options if provided
            if (tools != null && tools.Count > 0)
            {
                logger?.LogDebug("  ├─ {ToolCount} tools requested (not yet translated to MEAI functions)", tools.Count);
                // TODO Phase 1.8: Translate Tool objects to AIFunction declarations via AIFunctionFactory.Create
                // For now, we parse them to log but don't yet execute tool-calling flows.
            }

            logger?.LogInformation("⏳ Getting non-streaming response from chat client");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var selectedClient = await ResolveClientAsync(model, chatClient, modelSelectionResolver, logger, ct);
            
            var completion = await selectedClient.GetResponseAsync(messages, options, ct);
            
            sw.Stop();
            logger?.LogInformation("✅ Received response in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            var id = $"chatcmpl-{Guid.NewGuid().ToString("N").Substring(0, 24)}";
            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var responseText = completion.Messages?.FirstOrDefault()?.Text ?? "";
            var choices = new List<Choice>
            {
                new(
                    Index: 0,
                    Message: new ChatMessageDto(Role: "assistant", Content: responseText),
                    Delta: null,
                    FinishReason: "stop"
                )
            };

            var result = new ChatCompletionResponse(
                Id: id,
                Object: "chat.completion",
                Created: created,
                Model: model,
                Choices: choices,
                Usage: null
            );

            logger?.LogDebug("  └─ Response text length: {TextLength}", responseText.Length);
            return Results.Json(result);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "❌ Error in non-streaming handler");
            return Results.StatusCode(500);
        }
    }

    private static async Task<IChatClient> ResolveClientAsync(
        string model,
        IChatClient defaultClient,
        IModelSelectionResolver modelSelectionResolver,
        ILogger<ChatCompletionRequest>? logger,
        CancellationToken cancellationToken)
    {
        var selectedClient = await modelSelectionResolver.ResolveAsync(model, cancellationToken);
        if (selectedClient is not null)
        {
            logger?.LogInformation("🎛️ Honoring selected model {Model}", model);
            return selectedClient;
        }

        logger?.LogInformation("🧭 No direct client match for model {Model}; using routed default client", model);
        return defaultClient;
    }
}
