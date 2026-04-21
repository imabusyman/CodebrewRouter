namespace Blaze.LlmGateway.Api;

/// <summary>Chat completion request (OpenAI-compatible)</summary>
public record ChatCompletionRequest(
    string Model,
    IList<ChatMessageDto> Messages,
    float? Temperature = null,
    int? MaxTokens = null,
    bool Stream = false,
    float? TopP = null,
    float? FrequencyPenalty = null,
    float? PresencePenalty = null,
    IList<Tool>? Tools = null);

/// <summary>Chat message DTO for requests</summary>
public record ChatMessageDto(
    string Role,
    string Content);

/// <summary>Tool specification</summary>
public record Tool(
    string Type,
    FunctionDefinition Function);

/// <summary>Function definition for tools</summary>
public record FunctionDefinition(
    string Name,
    string? Description = null,
    object? Parameters = null);

/// <summary>Text completion request (legacy)</summary>
public record TextCompletionRequest(
    string Model,
    string Prompt,
    int? MaxTokens = null,
    float? Temperature = null,
    float? TopP = null,
    float? FrequencyPenalty = null,
    float? PresencePenalty = null,
    bool Stream = false);

/// <summary>Chat completion response</summary>
public record ChatCompletionResponse(
    string Id,
    string Object,
    long Created,
    string Model,
    IList<Choice> Choices,
    Usage? Usage = null);

/// <summary>Choice in chat completion response</summary>
public record Choice(
    int Index,
    ChatMessageDto? Message,
    ChoiceDelta? Delta,
    string? FinishReason);

/// <summary>Delta content for streaming responses</summary>
public record ChoiceDelta(
    string? Role = null,
    string? Content = null);

/// <summary>Text completion response</summary>
public record TextCompletionResponse(
    string Id,
    string Object,
    long Created,
    string Model,
    IList<TextChoice> Choices,
    Usage? Usage = null);

/// <summary>Text choice in completion response</summary>
public record TextChoice(
    int Index,
    string Text,
    string? FinishReason);

/// <summary>Token usage information</summary>
public record Usage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

/// <summary>Models list response</summary>
public record ModelsResponse(
    string Object,
    IList<ModelInfo> Data);

/// <summary>Model information</summary>
public record ModelInfo(
    string Id,
    string Object,
    string Provider,
    string? OwnedBy = null);
