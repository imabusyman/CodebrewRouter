using System.ClientModel;
using System.Runtime.CompilerServices;
using Blaze.LlmGateway.Core.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Responses;

namespace Blaze.LlmGateway.Infrastructure;

#pragma warning disable OPENAI001
/// <summary>
/// MEAI adapter for Azure AI Foundry project endpoints that expose OpenAI's Responses API.
/// </summary>
public sealed class FoundryResponsesChatClient(
    AzureFoundryOptions options,
    ILogger<FoundryResponsesChatClient> logger) : IChatClient
{
    private readonly ResponsesClient responsesClient = CreateClient(options);

    public void Dispose()
    {
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await responsesClient.CreateResponseAsync(
            CreateResponseOptions(chatMessages, options, streaming: false),
            cancellationToken);

        var text = ExtractText(response.Value);
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var stream = responsesClient.CreateResponseStreamingAsync(
            CreateResponseOptions(chatMessages, options, streaming: true),
            cancellationToken);

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            switch (update)
            {
                case StreamingResponseOutputTextDeltaUpdate textDelta
                    when !string.IsNullOrEmpty(textDelta.Delta):
                    yield return new ChatResponseUpdate(ChatRole.Assistant, textDelta.Delta);
                    break;

                case StreamingResponseErrorUpdate error:
                    throw new InvalidOperationException($"Foundry Responses API error: {error.Code} {error.Message}");

                case StreamingResponseFailedUpdate failed:
                    throw new InvalidOperationException(
                        $"Foundry Responses API failed: {failed.Response.Error?.Code} {failed.Response.Error?.Message}");
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    private CreateResponseOptions CreateResponseOptions(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? chatOptions,
        bool streaming)
    {
        var responseOptions = new CreateResponseOptions
        {
            Model = options.Model,
            Temperature = chatOptions?.Temperature,
            TopP = chatOptions?.TopP,
            MaxOutputTokenCount = chatOptions?.MaxOutputTokens,
            StreamingEnabled = streaming
        };

        foreach (var message in chatMessages)
        {
            var text = message.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            responseOptions.InputItems.Add(message.Role == ChatRole.System
                ? ResponseItem.CreateSystemMessageItem(text)
                : message.Role == ChatRole.Assistant
                    ? ResponseItem.CreateAssistantMessageItem(text, [])
                    : ResponseItem.CreateUserMessageItem(text));
        }

        logger.LogDebug(
            "Prepared Foundry Responses request for model {Model} with {InputCount} input item(s)",
            responseOptions.Model,
            responseOptions.InputItems.Count);

        return responseOptions;
    }

    private static string ExtractText(ResponseResult response)
    {
        var parts = response.OutputItems
            .OfType<MessageResponseItem>()
            .SelectMany(item => item.Content)
            .Select(part => part.Text)
            .Where(text => !string.IsNullOrEmpty(text));

        return string.Concat(parts);
    }

    private static ResponsesClient CreateClient(AzureFoundryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("AzureFoundry Responses API requires LlmGateway:Providers:AzureFoundry:ApiKey.");
        }

        var endpoint = NormalizeResponsesEndpoint(options.ResponsesEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("AzureFoundry Responses API requires LlmGateway:Providers:AzureFoundry:ResponsesEndpoint.");
        }

        return new ResponsesClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            });
    }

    private static string NormalizeResponsesEndpoint(string endpoint)
    {
        var normalized = endpoint.TrimEnd('/');
        const string responsesSuffix = "/responses";

        return normalized.EndsWith(responsesSuffix, StringComparison.OrdinalIgnoreCase)
            ? normalized[..^responsesSuffix.Length]
            : normalized;
    }
}
#pragma warning restore OPENAI001
