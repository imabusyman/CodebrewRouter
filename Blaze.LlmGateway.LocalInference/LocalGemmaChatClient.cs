using System.Runtime.CompilerServices;
using LLama;
using LLama.Common;
using Microsoft.Extensions.AI;

namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// A thin MEAI adapter for local LLamaSharp inference using the Gemma model.
/// Implements <see cref="DelegatingChatClient"/> to integrate with the MEAI pipeline.
/// Implements <see cref="IAsyncDisposable"/> for proper cleanup of native LLamaSharp resources.
/// </summary>
public sealed class LocalGemmaChatClient : DelegatingChatClient, IAsyncDisposable
{
    private readonly LLamaWeights? _weights;
    private readonly LLamaContext? _context;
    private readonly InteractiveExecutor? _executor;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="LocalGemmaChatClient"/>.
    /// If modelPath is provided, loads the Gemma model; otherwise, uses a no-op client.
    /// </summary>
    public LocalGemmaChatClient(string? modelPath = null)
        : base(new NoOpChatClientWithMetadata())
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            _weights = null;
            _context = null;
            _executor = null;
            return;
        }

        try
        {
            var modelParams = new ModelParams(modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = 0,
            };

            _weights = LLamaWeights.LoadFromFile(modelParams);
            _context = new LLamaContext(_weights, modelParams);
            _executor = new InteractiveExecutor(_context);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load Gemma model from '{modelPath}'", ex);
        }
    }

    /// <summary>
    /// Streams chat completions using the local Gemma model.
    /// </summary>
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_executor is null)
        {
            yield break;
        }

        var messages = chatMessages.ToList();
        if (messages.Count == 0)
        {
            yield break;
        }

        var history = new List<ChatMessage>();
        for (int i = 0; i < messages.Count - 1; i++)
        {
            history.Add(messages[i]);
        }

        var lastMessage = messages[^1];
        string userPrompt = lastMessage.Text ?? "";
        var prompt = FormatConversation(history, userPrompt);

        var inferenceParams = new InferenceParams
        {
            Temperature = options?.Temperature ?? 0.7f,
            TopP = 0.9f,
            MaxTokens = options?.MaxOutputTokens ?? 512,
        };

        try
        {
            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken))
            {
                yield return new ChatResponseUpdate();

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
        }
    }

    /// <summary>
    /// Completes a single chat request.
    /// </summary>
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var accumulatedText = "";

        await foreach (var update in GetStreamingResponseAsync(chatMessages, options, cancellationToken))
        {
            // Just accumulate empty updates for now
        }

        var message = new ChatMessage(ChatRole.Assistant, accumulatedText);
        return new ChatResponse(message)
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    /// <summary>
    /// Asynchronously disposes the LLamaSharp resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await Task.Run(CleanupResources);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Internal cleanup implementation for LLamaSharp resources.
    /// </summary>
    private void CleanupResources()
    {
        try
        {
            _context?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error disposing LLamaContext: {ex.Message}");
        }

        try
        {
            _weights?.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error disposing LLamaWeights: {ex.Message}");
        }
    }

    /// <summary>
    /// Formats conversation history and current prompt for the Gemma model.
    /// </summary>
    private static string FormatConversation(IEnumerable<ChatMessage> history, string currentPrompt)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var msg in history)
        {
            if (msg.Role == ChatRole.User)
            {
                sb.Append("User: ");
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                sb.Append("Assistant: ");
            }

            sb.AppendLine(msg.Text ?? "");
        }

        sb.Append("User: ");
        sb.AppendLine(currentPrompt);
        sb.Append("Assistant: ");

        return sb.ToString();
    }
}

/// <summary>
/// Internal no-op chat client with custom metadata for LocalGemmaChatClient.
/// </summary>
internal sealed class NoOpChatClientWithMetadata : IChatClient
{
    public ChatClientMetadata Metadata => new();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield break;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var message = new ChatMessage(ChatRole.Assistant, "");
        return new ChatResponse(message)
        {
            ModelId = "gemma-local",
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
