using System;
using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blaze.LlmGateway.LocalInference;

/// <summary>
/// Factory for instantiating hybrid routing strategies.
///
/// Constructs a <see cref="HybridLocalRemoteRoutingStrategy"/> that:
/// 1. Attempts to route to local Gemma if available and healthy
/// 2. Falls back to a remote provider routing strategy (OllamaMetaRoutingStrategy or KeywordRoutingStrategy)
///
/// The factory encapsulates strategy composition and lifetime management.
/// </summary>
public class HybridRoutingStrategyFactory
{
    private readonly LocalInferenceOptions _localInferenceOptions;
    private readonly IModelDistributionProvider _modelProvider;
    private readonly IChatClient? _routerClient;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<HybridLocalRemoteRoutingStrategy> _logger;

    /// <summary>
    /// Constructs a factory that builds hybrid routing strategies.
    /// </summary>
    /// <param name="localInferenceOptions">Configuration for local Gemma inference.</param>
    /// <param name="modelProvider">Provider for model distribution (download/cache).</param>
    /// <param name="routerClient">Optional Ollama router client for meta-routing. If null, keyword-based routing is used as fallback.</param>
    /// <param name="loggerFactory">Logger factory for creating loggers.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public HybridRoutingStrategyFactory(
        IOptions<LocalInferenceOptions> localInferenceOptions,
        IModelDistributionProvider modelProvider,
        IChatClient? routerClient,
        ILoggerFactory loggerFactory,
        ILogger<HybridLocalRemoteRoutingStrategy> logger)
    {
        _localInferenceOptions = localInferenceOptions?.Value ?? throw new ArgumentNullException(nameof(localInferenceOptions));
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _routerClient = routerClient;
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Constructs a hybrid routing strategy with the configured components.
    /// </summary>
    /// <returns>A new <see cref="HybridLocalRemoteRoutingStrategy"/> instance.</returns>
    /// <remarks>
    /// The fallback strategy hierarchy is:
    /// 1. If routerClient is available → OllamaMetaRoutingStrategy wrapping KeywordRoutingStrategy
    /// 2. Else → KeywordRoutingStrategy only
    /// </remarks>
    public IRoutingStrategy CreateStrategy()
    {
        var fallbackStrategy = BuildFallbackStrategy();
        return new HybridLocalRemoteRoutingStrategy(
            _localInferenceOptions,
            _modelProvider,
            fallbackStrategy,
            _logger);
    }

    /// <summary>
    /// Builds the fallback strategy chain used when local Gemma is unavailable.
    /// </summary>
    private IRoutingStrategy BuildFallbackStrategy()
    {
        var keywordStrategy = new KeywordRoutingStrategy(
            _loggerFactory.CreateLogger<KeywordRoutingStrategy>(),
            RouteDestination.OllamaRouter);

        if (_routerClient is null)
        {
            return keywordStrategy;
        }

        return new OllamaMetaRoutingStrategy(
            _routerClient,
            keywordStrategy,
            _loggerFactory.CreateLogger<OllamaMetaRoutingStrategy>());
    }
}

