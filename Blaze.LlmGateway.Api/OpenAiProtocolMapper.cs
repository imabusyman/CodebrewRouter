using System.Text.Json;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.Api;

internal static class OpenAiProtocolMapper
{
    public static List<ChatMessage> ToChatMessages(JsonElement input)
    {
        if (input.ValueKind is JsonValueKind.String)
        {
            return [new ChatMessage(ChatRole.User, input.GetString() ?? string.Empty)];
        }

        if (input.ValueKind != JsonValueKind.Array)
        {
            return [new ChatMessage(ChatRole.User, input.GetRawText())];
        }

        var messages = new List<ChatMessage>();
        foreach (var item in input.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                messages.Add(new ChatMessage(ChatRole.User, item.GetString() ?? string.Empty));
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var role = item.TryGetProperty("role", out var roleElement)
                ? roleElement.GetString()
                : "user";
            var content = ExtractText(item.TryGetProperty("content", out var contentElement)
                ? contentElement
                : item);

            messages.Add(new ChatMessage(ToChatRole(role), content));
        }

        return messages.Count == 0
            ? [new ChatMessage(ChatRole.User, input.GetRawText())]
            : messages;
    }

    public static List<ChatMessage> ApplyInstructions(
        IList<ChatMessage> messages,
        string? instructions,
        string model,
        IOptions<LlmGatewayOptions> gatewayOptions)
    {
        var profile = gatewayOptions.Value.FindVirtualModel(model);
        var systemPrompts = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(profile?.SystemPrompt))
        {
            systemPrompts.Add(new ChatMessage(ChatRole.System, profile.SystemPrompt));
        }

        if (!string.IsNullOrWhiteSpace(instructions))
        {
            systemPrompts.Add(new ChatMessage(ChatRole.System, instructions));
        }

        return systemPrompts.Count == 0
            ? [.. messages]
            : [.. systemPrompts, .. messages];
    }

    public static async Task<IChatClient> ResolveClientAsync(
        string model,
        IChatClient chatClient,
        IModelSelectionResolver modelSelectionResolver,
        IModelAvailabilityRegistry availabilityRegistry,
        CancellationToken cancellationToken)
    {
        var selected = await modelSelectionResolver.ResolveAsync(model, cancellationToken);
        if (selected is not null)
        {
            return selected;
        }

        var configuredModel = availabilityRegistry.FindModel(model, includeUnavailable: true);
        if (configuredModel is { Enabled: false })
        {
            throw new InvalidOperationException(configuredModel.ErrorMessage ?? $"Model '{model}' is unavailable.");
        }

        return chatClient;
    }

    public static ChatOptions ToChatOptions(CreateResponseRequest request)
        => new()
        {
            ModelId = request.Model,
            Temperature = request.Temperature,
            TopP = request.TopP,
            MaxOutputTokens = request.MaxOutputTokens ?? request.MaxCompletionTokens,
            AllowMultipleToolCalls = request.ParallelToolCalls
        };

    public static ResponseObject ToResponseObject(
        CreateResponseRequest request,
        ChatResponse completion,
        string? conversationId = null,
        string? responseId = null)
    {
        var output = new List<ResponseOutputItem>();
        var outputText = completion.Text ?? string.Empty;

        foreach (var message in completion.Messages ?? [])
        {
            if (!string.IsNullOrEmpty(message.Text))
            {
                output.Add(new ResponseOutputItem(
                    Id: Ids.New("msg"),
                    Type: "message",
                    Status: "completed",
                    Role: ResolveOpenAiRole(message.Role),
                    Content:
                    [
                        new ResponseContentPart(
                            Type: "output_text",
                            Text: message.Text)
                    ]));
            }

            foreach (var toolCall in message.Contents.OfType<FunctionCallContent>())
            {
                output.Add(new ResponseOutputItem(
                    Id: Ids.New("fc"),
                    Type: "function_call",
                    Status: "completed",
                    CallId: string.IsNullOrWhiteSpace(toolCall.CallId) ? Ids.New("call") : toolCall.CallId,
                    Name: toolCall.Name,
                    Arguments: JsonSerializer.Serialize(toolCall.Arguments)));
            }

            foreach (var toolResult in message.Contents.OfType<FunctionResultContent>())
            {
                output.Add(new ResponseOutputItem(
                    Id: Ids.New("fco"),
                    Type: "function_call_output",
                    Status: "completed",
                    CallId: toolResult.CallId,
                    Output: toolResult.Result?.ToString()));
            }
        }

        if (output.Count == 0)
        {
            output.Add(new ResponseOutputItem(
                Id: Ids.New("msg"),
                Type: "message",
                Status: "completed",
                Role: "assistant",
                Content: [new ResponseContentPart("output_text", outputText)]));
        }

        var usage = completion.Usage is null
            ? null
            : new Usage(
                PromptTokens: ToInt(completion.Usage.InputTokenCount),
                CompletionTokens: ToInt(completion.Usage.OutputTokenCount),
                TotalTokens: ToInt(
                    completion.Usage.TotalTokenCount ??
                    (completion.Usage.InputTokenCount.GetValueOrDefault() + completion.Usage.OutputTokenCount.GetValueOrDefault())));

        return new ResponseObject(
            Id: responseId ?? Ids.New("resp"),
            Object: "response",
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status: "completed",
            Model: request.Model,
            Output: output,
            OutputText: outputText,
            ConversationId: conversationId,
            PreviousResponseId: request.PreviousResponseId,
            Metadata: request.Metadata,
            Usage: usage);
    }

    public static string ExtractText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var part in element.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.String)
                {
                    parts.Add(part.GetString() ?? string.Empty);
                    continue;
                }

                if (part.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    parts.Add(text.GetString() ?? string.Empty);
                }
            }

            return string.Join("\n", parts.Where(static text => !string.IsNullOrWhiteSpace(text)));
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("text", out var textElement) &&
            textElement.ValueKind == JsonValueKind.String)
        {
            return textElement.GetString() ?? string.Empty;
        }

        return element.GetRawText();
    }

    public static ConversationItem ToConversationItem(ChatMessage message)
        => new(
            Type: "message",
            Role: ResolveOpenAiRole(message.Role),
            Content: message.Text,
            Id: Ids.New("msg"),
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public static ConversationItem ToConversationItem(ResponseOutputItem item)
        => new(
            Type: item.Type,
            Role: item.Role,
            Content: item.Content is { Count: > 0 }
                ? string.Join("\n", item.Content.Select(content => content.Text).Where(static text => !string.IsNullOrWhiteSpace(text)))
                : item.Output ?? item.Arguments,
            Id: item.Id,
            CreatedAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static ChatRole ToChatRole(string? role)
        => role?.ToLowerInvariant() switch
        {
            "system" or "developer" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            "tool" or "function" => ChatRole.Tool,
            _ => ChatRole.User
        };

    private static string ResolveOpenAiRole(ChatRole role)
    {
        if (role == ChatRole.System)
        {
            return "system";
        }

        if (role == ChatRole.Assistant)
        {
            return "assistant";
        }

        return role == ChatRole.Tool ? "tool" : "user";
    }

    private static int ToInt(long? value)
        => value is null
            ? 0
            : value > int.MaxValue
                ? int.MaxValue
                : (int)value.Value;
}
