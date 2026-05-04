namespace Blaze.LlmGateway.Core.Routing;

public readonly record struct RouterStartEvent(int MessageCount);

public readonly record struct RouterCleanEvent(int OriginalChars, int CleanedChars, long ElapsedMs);

public readonly record struct RouterResolveEvent(
    string TaskType,
    int TokenCount,
    int ProviderCount,
    string ProviderChain,
    long ElapsedMs);

public readonly record struct RouterContextBudgetEvent(
    int Attempt,
    string Key,
    string Model,
    int CurrentTokens,
    int InputBudget,
    int MaxContext);

public readonly record struct RouterTryEvent(
    int Attempt,
    int Total,
    string Key,
    string Model,
    string TaskType);

public readonly record struct RouterProbeEvent(
    int Attempt,
    string Key,
    string Model,
    long FirstChunkMs,
    bool Success);

public readonly record struct RouterSuccessEvent(
    int Attempt,
    string Key,
    string Model,
    string TaskType,
    string? FinishReason,
    int? InputTokens,
    int? OutputTokens,
    long ElapsedMs);

public readonly record struct RouterFailEvent(
    int Attempt,
    string Key,
    string Model,
    string Message);

public readonly record struct RouterCompactEvent(
    int Attempt,
    string Key,
    int BeforeTokens,
    int AfterTokens);

public readonly record struct RouterSkipEvent(
    int Attempt,
    string Key,
    string Model,
    int CurrentTokens,
    int Budget);

public readonly record struct RouterExhaustedEvent(
    int TotalAttempted,
    string TaskType,
    string FallbackKey);

public readonly record struct RouterMidstreamFailEvent(
    string Key,
    string Model);

public readonly record struct RouterStreamCompleteEvent(
    int ChunkCount,
    string Key,
    string Model,
    string TaskType,
    long ElapsedMs);
