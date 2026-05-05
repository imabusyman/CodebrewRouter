# CodebrewRouterProvider Quickstart Guide

## Overview

**CodebrewRouterProvider** is a unified, composable DI abstraction that brings intelligent LLM routing and model availability management to any .NET environment—whether mobile (MAUI), desktop, or cloud-orchestrated (Aspire).

Built on `Microsoft.Extensions.AI` (MEAI), the provider offers three core capabilities:

1. **Local Model Routing** — Route requests to local Ollama instances with intelligent provider selection
2. **Model Availability Tracking** — Automatic health checks and model availability discovery
3. **Hybrid Failover** — Seamless fallback between local and remote providers

### Three Deployment Scenarios

| Scenario | Best For | Setup Time | Features |
|----------|----------|------------|----------|
| **Mobile (MAUI)** | iOS/Android apps needing offline-first LLM capability | 2 minutes | Core chat, local availability tracking, no remote discovery |
| **Desktop** | Windows/macOS applications with optional remote fallback | 5 minutes | All Phase 1 features: health checks, discovery, routing, hybrid failover |
| **Aspire** | Cloud-native .NET orchestration with multiple LLM providers | 10 minutes | All Phase 1 + centralized health aggregation, resource provisioning |

### Key Benefits

- **Unified API** across mobile, desktop, and cloud environments
- **Mobile-first** design using `IOptions<T>` pattern (no `IConfiguration` required)
- **Composable** fluent builder for opt-in advanced features
- **Testable** with test doubles and mock services built-in
- **Zero-configuration** defaults that work out of the box for basic scenarios
- **Backwards compatible** with existing `AddLocalInferenceServices(IConfiguration)` code

---

## Installation

### Prerequisites

- **.NET 8+** (or .NET 10 for the latest features)
- **Ollama** running locally (for local model execution) — [Download Ollama](https://ollama.ai)
- **Optional:** Remote LLM provider credentials (Azure Foundry, GitHub Models, etc.)

### Package Setup

`CodebrewRouterProvider` is **not available on NuGet**. It is part of the `Blaze.LlmGateway.Infrastructure` project within this repository.

If you are using **Blaze.LlmGateway** as a reference or forking the solution:

1. Ensure `Blaze.LlmGateway.Infrastructure` is included in your project references
2. Ensure `Blaze.LlmGateway.Core` is referenced (for domain types like `RouteDestination`)
3. Ensure all peer dependencies (`Microsoft.Extensions.AI`, `OllamaSharp`, etc.) are installed

For **external projects**, consider one of these approaches:

- **Extract the provider classes** into a shared library and reference it
- **Copy the source** directly into your infrastructure layer
- **Create a NuGet package** from this source and publish to your internal feed

---

## Minimal Usage (Mobile/MAUI)

For mobile applications that need offline-first LLM capability, CodebrewRouterProvider requires just a few lines of setup.

### Scenario: MAUI App with Local Ollama

```csharp
// MauiProgram.cs
using Blaze.LlmGateway.Infrastructure;
using Blaze.LlmGateway.LocalInference;
using Microsoft.Extensions.DependencyInjection;

var builder = MauiApp.CreateBuilder();

var services = new ServiceCollection();

// Minimal setup: just provide the local Ollama endpoint
var mobileOptions = new CodebrewRouterProviderOptions
{
    LocalEndpoint = new Uri("http://127.0.0.1:11434")
};

services.AddCodebrewRouterProvider(mobileOptions);

// Now you can inject and use IChatClient
var app = builder.Build();
// ...
```

### What You Get

- ✅ **Local model execution** via Ollama
- ✅ **Automatic availability tracking** (checks every 30 seconds by default)
- ✅ **Basic health checks** on the local endpoint
- ❌ Remote discovery disabled (no network overhead)
- ❌ Health check endpoint not exposed (mobile apps don't typically expose HTTP endpoints)

### Default Behavior

The minimal configuration uses these defaults:

```csharp
var defaults = new CodebrewRouterProviderOptions
{
    LocalEndpoint = new Uri("http://127.0.0.1:11434"),           // Required
    RemoteDiscoveryEndpoint = null,                              // Disabled
    HealthCheckOptions = new HealthCheckOptions 
    { 
        Enabled = false                                           // No health endpoint
    },
    DiscoveryOptions = new DiscoveryOptions 
    { 
        PollingIntervalSeconds = 30,
        TimeoutSeconds = 5,
        Enabled = false                                           // No remote discovery
    },
    RoutingOptions = new RoutingOptions 
    { 
        FallbackStrategyType = "Keyword"                          // Simple keyword-based fallback
    },
    CacheAvailabilityTtlSeconds = 60,
    TestMode = false
};
```

### Expected Output

When you make a request to the chat endpoint:

```
[INFO] CodebrewRouterProvider: Initializing local inference stack
[DEBUG] LocalModelAvailability: Polling local Ollama at http://127.0.0.1:11434
[DEBUG] LocalModelAvailability: Found 2 available models: [gemma2, mistral]
[INFO] Chat request routed to Ollama (local)
[STREAM] Streaming response from gemma2...
```

---

## Full Usage (Desktop)

Desktop applications typically benefit from **all Phase 1 features**: health checks, model discovery, intelligent routing, and optional remote fallback.

### Scenario: Desktop App with Hybrid Routing

```csharp
// Program.cs or AppStartup.cs
using Blaze.LlmGateway.Infrastructure;
using Blaze.LlmGateway.LocalInference;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Full configuration with all features enabled
var desktopOptions = new CodebrewRouterProviderOptions
{
    LocalEndpoint = new Uri("http://localhost:11434"),
    RemoteDiscoveryEndpoint = new Uri("http://localhost:5273"),
    CacheAvailabilityTtlSeconds = 60,
    DiscoveryPollingIntervalSeconds = 30
};

// Build the full stack with fluent builder
services
    .AddCodebrewRouterProvider(desktopOptions)
    .WithHealthChecks(hcOptions =>
    {
        hcOptions.Enabled = true;
        hcOptions.ExposureEndpoint = "/health/llm";  // Optional: expose health checks
    })
    .WithDiscovery(discoveryOptions =>
    {
        discoveryOptions.Enabled = true;
        discoveryOptions.PollingIntervalSeconds = 30;
        discoveryOptions.TimeoutSeconds = 5;
        discoveryOptions.CircuitBreakerThreshold = 3;  // Fail after 3 consecutive errors
    })
    .WithRouting(routingOptions =>
    {
        routingOptions.FallbackStrategyType = "Keyword";
        routingOptions.HybridRoutingEnabled = true;
    })
    .Build();

// Use the provider
var provider = services.BuildServiceProvider();
var chatClient = provider.GetRequiredService<IChatClient>();

// Make requests (automatically routed based on content analysis)
var messages = new[] 
{ 
    new ChatMessage(ChatRole.User, "What's the capital of France?") 
};

await foreach (var chunk in chatClient.GetStreamingResponseAsync(messages))
{
    Console.Write(chunk.Content ?? "");
}
```

### What You Get

- ✅ **Local model execution** with Ollama
- ✅ **Automatic model discovery** (polls remote endpoint every 30 seconds)
- ✅ **Health checks** (both local and remote endpoints monitored)
- ✅ **Intelligent routing** — requests classified to best provider
- ✅ **Hybrid failover** — automatic fallback if local service fails
- ✅ **Circuit breaker** — prevents cascading failures during outages

### Feature Breakdown

#### Health Checks
```csharp
.WithHealthChecks(options =>
{
    options.Enabled = true;                          // Enable health monitoring
    options.ExposureEndpoint = "/health/llm";        // Optional HTTP endpoint
    options.Timeout = TimeSpan.FromSeconds(5);       // Health check timeout
})
```

#### Model Discovery
```csharp
.WithDiscovery(options =>
{
    options.Enabled = true;
    options.PollingIntervalSeconds = 30;             // Query remote every 30s
    options.TimeoutSeconds = 5;                      // Per-request timeout
    options.CircuitBreakerThreshold = 3;             // Fail after 3 errors
    options.CircuitBreakerResetMinutes = 5;          // Reset after 5 minutes
})
```

#### Routing Strategy
```csharp
.WithRouting(options =>
{
    options.FallbackStrategyType = "Keyword";        // "Keyword" or "OllamaMeta"
    options.HybridRoutingEnabled = true;             // Try local first, fall back to remote
})
```

### Configuration Tuning

**For high-latency networks or mobile:**
```csharp
discoveryOptions.TimeoutSeconds = 10;               // Increase timeout
discoveryOptions.PollingIntervalSeconds = 300;      // Reduce polling (5 minutes)
```

**For low-latency local networks:**
```csharp
discoveryOptions.TimeoutSeconds = 2;                // Aggressive timeout
discoveryOptions.PollingIntervalSeconds = 10;       // Frequent polling
```

**For testing with circuit breaker recovery:**
```csharp
discoveryOptions.CircuitBreakerThreshold = 1;       // Fail fast in tests
discoveryOptions.CircuitBreakerResetMinutes = 1;    // Reset quickly
```

---

## Aspire Usage

When running under **Aspire orchestration**, CodebrewRouterProvider can be configured both the **new way** and the **old (deprecated) way**. Both continue to work for backwards compatibility.

### New API (Recommended)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure options from Aspire settings
var aspireOptions = new CodebrewRouterProviderOptions
{
    LocalEndpoint = builder.Configuration["LlmGateway:LocalEndpoint"] 
        ?? new Uri("http://localhost:11434"),
    RemoteDiscoveryEndpoint = builder.Configuration["LlmGateway:RemoteEndpoint"] 
        ?? new Uri("http://localhost:5273")
};

// Register using new API
builder.Services
    .AddCodebrewRouterProvider(aspireOptions)
    .WithHealthChecks()
    .WithDiscovery()
    .WithRouting()
    .Build();

// Also add Aspire defaults
builder.AddServiceDefaults();
```

### Old API (Deprecated, Still Works)

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Old way: still supported, but marked [Obsolete]
builder.Services.AddLocalInferenceServices(builder.Configuration);

// Add Aspire defaults
builder.AddServiceDefaults();
```

### Configuration Binding from appsettings.json

```csharp
// Program.cs
var section = builder.Configuration.GetSection("LlmGateway");
var options = new CodebrewRouterProviderOptions();
section.Bind(options);

builder.Services.AddCodebrewRouterProvider(options);
```

### appsettings.json

```json
{
  "LlmGateway": {
    "LocalEndpoint": "http://ollama-local:11434",
    "RemoteDiscoveryEndpoint": "http://codebrewrouter-remote:5273",
    "CacheAvailabilityTtlSeconds": 60,
    "DiscoveryPollingIntervalSeconds": 30,
    "HealthCheckOptions": {
      "Enabled": true
    },
    "DiscoveryOptions": {
      "Enabled": true,
      "PollingIntervalSeconds": 30,
      "TimeoutSeconds": 5
    },
    "RoutingOptions": {
      "FallbackStrategyType": "Keyword",
      "HybridRoutingEnabled": true
    }
  }
}
```

### Migration Timeline

| Timeline | Status |
|----------|--------|
| **v1.0 - v1.x** | Both APIs work. New API preferred. Old API marked `[Obsolete]`. |
| **v2.0+** | Old API removed. Only new API supported. |

---

## Configuration Reference

### CodebrewRouterProviderOptions

All options are specified as a `CodebrewRouterProviderOptions` instance:

```csharp
public class CodebrewRouterProviderOptions
{
    /// <summary>
    /// Required. Local Ollama endpoint (e.g., "http://127.0.0.1:11434")
    /// </summary>
    public Uri LocalEndpoint { get; set; } = null!;

    /// <summary>
    /// Optional. Remote discovery endpoint for hybrid routing
    /// </summary>
    public Uri? RemoteDiscoveryEndpoint { get; set; }

    /// <summary>
    /// How long to cache model availability before re-polling
    /// </summary>
    public int CacheAvailabilityTtlSeconds { get; set; } = 60;

    /// <summary>
    /// Health check configuration
    /// </summary>
    public HealthCheckOptions HealthCheckOptions { get; set; } = new();

    /// <summary>
    /// Model discovery configuration
    /// </summary>
    public DiscoveryOptions DiscoveryOptions { get; set; } = new();

    /// <summary>
    /// Routing strategy configuration
    /// </summary>
    public RoutingOptions RoutingOptions { get; set; } = new();

    /// <summary>
    /// Enable test mode (use mock services for unit testing)
    /// </summary>
    public bool TestMode { get; set; }
}
```

### HealthCheckOptions

```csharp
public class HealthCheckOptions
{
    /// <summary>
    /// Enable health checks (default: false for mobile, true for desktop)
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// HTTP endpoint to expose health checks (e.g., "/health/llm")
    /// Only used if Enabled = true
    /// </summary>
    public string? ExposureEndpoint { get; set; }

    /// <summary>
    /// Timeout per health check
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
```

### DiscoveryOptions

```csharp
public class DiscoveryOptions
{
    /// <summary>
    /// Enable remote model discovery (default: false for mobile, true for desktop)
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How often to poll for new models (seconds)
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Timeout per discovery request
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// How many consecutive failures before opening circuit breaker
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 3;

    /// <summary>
    /// How long circuit breaker stays open after threshold is hit
    /// </summary>
    public int CircuitBreakerResetMinutes { get; set; } = 5;
}
```

### RoutingOptions

```csharp
public class RoutingOptions
{
    /// <summary>
    /// Routing strategy to use: "Keyword" or "OllamaMeta"
    /// </summary>
    public string FallbackStrategyType { get; set; } = "Keyword";

    /// <summary>
    /// Enable hybrid routing (try local first, fall back to remote)
    /// </summary>
    public bool HybridRoutingEnabled { get; set; }
}
```

### Complete Reference Table

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LocalEndpoint` | `Uri` | **Required** | Local Ollama endpoint URL |
| `RemoteDiscoveryEndpoint` | `Uri?` | `null` | Remote provider endpoint for fallback |
| `CacheAvailabilityTtlSeconds` | `int` | `60` | Model cache duration (seconds) |
| `HealthCheckOptions.Enabled` | `bool` | `false` (mobile), `true` (desktop) | Enable health monitoring |
| `HealthCheckOptions.ExposureEndpoint` | `string?` | `null` | HTTP endpoint for health probes |
| `HealthCheckOptions.Timeout` | `TimeSpan` | `5s` | Health check timeout |
| `DiscoveryOptions.Enabled` | `bool` | `false` (mobile), `true` (desktop) | Enable remote discovery |
| `DiscoveryOptions.PollingIntervalSeconds` | `int` | `30` | Discovery poll frequency |
| `DiscoveryOptions.TimeoutSeconds` | `int` | `5` | Discovery request timeout |
| `DiscoveryOptions.CircuitBreakerThreshold` | `int` | `3` | Failures before circuit break |
| `DiscoveryOptions.CircuitBreakerResetMinutes` | `int` | `5` | Circuit breaker reset time |
| `RoutingOptions.FallbackStrategyType` | `string` | `"Keyword"` | Routing strategy name |
| `RoutingOptions.HybridRoutingEnabled` | `bool` | `false` | Try local before remote |
| `TestMode` | `bool` | `false` | Enable mock services for testing |

---

## Testing

### Using TestMode

For unit and integration tests, enable `TestMode` to use mock implementations:

```csharp
var testOptions = new CodebrewRouterProviderOptions
{
    LocalEndpoint = new Uri("http://localhost:11434"),
    TestMode = true  // ← Enables mock services
};

var services = new ServiceCollection();
services.AddCodebrewRouterProvider(testOptions);

var provider = services.BuildServiceProvider();
var chatClient = provider.GetRequiredService<IChatClient>();

// Now chatClient is a mock that returns predictable responses
```

### Unit Test Example (xUnit + Moq)

```csharp
public class ChatServiceTests
{
    [Fact]
    public async Task ChatClient_ReturnsResponse_WhenTestModeEnabled()
    {
        // Arrange
        var options = new CodebrewRouterProviderOptions
        {
            LocalEndpoint = new Uri("http://localhost:11434"),
            TestMode = true
        };

        var services = new ServiceCollection();
        services.AddCodebrewRouterProvider(options);
        var provider = services.BuildServiceProvider();

        var chatClient = provider.GetRequiredService<IChatClient>();

        var messages = new[] 
        { 
            new ChatMessage(ChatRole.User, "Say hello") 
        };

        // Act
        var response = await chatClient.GetResponseAsync(messages);

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response.Content ?? "");
    }
}
```

### Integration Test Example

```csharp
public class LocalInferenceIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalInference_RoutesChatRequest_ToOllama()
    {
        // Arrange
        var options = new CodebrewRouterProviderOptions
        {
            LocalEndpoint = new Uri("http://localhost:11434"),
            RemoteDiscoveryEndpoint = new Uri("http://localhost:5273"),
            TestMode = false  // Use real services
        };

        var services = new ServiceCollection();
        services
            .AddCodebrewRouterProvider(options)
            .WithHealthChecks()
            .WithDiscovery()
            .WithRouting()
            .Build();

        var provider = services.BuildServiceProvider();
        var chatClient = provider.GetRequiredService<IChatClient>();

        var messages = new[] 
        { 
            new ChatMessage(ChatRole.User, "What is 2+2?") 
        };

        // Act
        var response = await chatClient.GetResponseAsync(messages);

        // Assert
        Assert.NotNull(response);
        Assert.Contains("4", response.Content);
    }
}
```

### Mock Services Available

When `TestMode = true`, the following services are registered as test doubles:

- **`ILocalModelAvailability`** — Returns hardcoded list: `["test-model-1", "test-model-2"]`
- **`ICodebrewRouterDiscoveryService`** — Returns empty discovery results (no remote models)
- **`IChatClient`** — Returns mock responses (configurable per test)

---

## Troubleshooting

### Common Errors

#### Error: "LocalEndpoint is required"
**Cause:** `CodebrewRouterProviderOptions.LocalEndpoint` is null or not set.

**Fix:**
```csharp
var options = new CodebrewRouterProviderOptions
{
    LocalEndpoint = new Uri("http://127.0.0.1:11434")  // ← Add this
};
services.AddCodebrewRouterProvider(options);
```

#### Error: "TCP connection refused on localhost:11434"
**Cause:** Ollama is not running or not listening on the expected port.

**Fix:**
1. Install Ollama: https://ollama.ai/download
2. Start Ollama: `ollama serve`
3. Verify it's running: `curl http://localhost:11434/api/tags`

#### Error: "Health check failed after 5 seconds"
**Cause:** Network connectivity issue or Ollama timeout.

**Fix:**
```csharp
// Increase timeout for slow networks
.WithHealthChecks(options =>
{
    options.Timeout = TimeSpan.FromSeconds(10);  // Increase from 5s
})
```

#### Error: "Validation exception: Options are invalid"
**Cause:** Configuration binding failed or required properties are missing.

**Fix:**
```csharp
// Ensure all required properties are set
var options = new CodebrewRouterProviderOptions
{
    LocalEndpoint = new Uri("http://localhost:11434"),     // Required
    RemoteDiscoveryEndpoint = new Uri("..."),              // Optional but recommended
    CacheAvailabilityTtlSeconds = 60,                      // Required, has default
    TestMode = false                                        // Required, defaults to false
};
```

### Frequently Asked Questions

**Q: Can I use multiple CodebrewRouterProvider instances in the same app?**

A: Not recommended. CodebrewRouterProvider is designed as a single, gateway-level service. If you need multiple routing strategies, use the `WithRoutingStrategy<T>()` builder method to customize the strategy.

**Q: Is CodebrewRouterProvider thread-safe?**

A: Yes. All services registered by CodebrewRouterProvider use thread-safe implementations. Async operations are safe for concurrent calls.

**Q: Can I switch routing strategies at runtime?**

A: Yes, via the builder:
```csharp
services.AddCodebrewRouterProvider(options)
    .WithRoutingStrategy<CustomRoutingStrategy>()
    .Build();
```

**Q: What if Ollama crashes while my app is running?**

A: CodebrewRouterProvider will detect the failure via health checks and automatically disable local routing. Remote fallback will take over if configured. Once Ollama restarts, health checks will re-enable local routing.

**Q: Do I need to call `.Build()` explicitly?**

A: No. The builder is finalized automatically when you call `BuildServiceProvider()`. However, calling `.Build()` explicitly improves readability and can help catch configuration errors earlier.

---

## Next Steps

### Phase 2: Observability
Add OpenTelemetry metrics and tracing:
- Request latency per provider
- Model availability timeline
- Failover events
- Circuit breaker state changes

### Phase 3: Resilience
Implement advanced resilience patterns:
- Automatic retry with exponential backoff
- Mid-stream failover for long-running requests
- Provider capacity detection and load balancing
- Graceful degradation under high load

### Phase 4: MCP Tool Integration
Extend CodebrewRouterProvider with Model Context Protocol (MCP) support:
- Automatic tool registration from MCP servers
- Tool result caching
- Cross-provider tool availability

### Phase 5: Advanced Routing
Extend routing capabilities:
- Cost-aware routing (pay-per-token optimization)
- Latency SLA enforcement
- Token budget management
- Multi-hop routing (chain multiple providers)

### Additional Resources

- **[Design Specification](../../superpowers/specs/2026-05-04-codebrewrouter-provider-design.md)** — Detailed architecture and decisions
- **[Migration Guide](./MIGRATION-LocalInferenceToProvider.md)** — Upgrade from LocalInferenceServices
- **[Main README](../../README.md)** — Blaze.LlmGateway overview
- **[API Reference](../../CLAUDE.md)** — MEAI fundamentals and conventions
- **[GitHub Repository](https://github.com/imabusyman/CodebrewRouter)** — Source code and issues

---

## Summary

CodebrewRouterProvider brings enterprise-grade LLM routing and availability management to all .NET environments. Whether you're building a mobile app, desktop application, or cloud service, the same unified API gives you:

- ✅ Automatic local model management
- ✅ Intelligent provider routing
- ✅ Health monitoring and failover
- ✅ Zero-configuration defaults
- ✅ Full testability with test doubles

**Get started in minutes.** Choose your scenario above and copy the code example. Ollama will handle the rest.

---

**Last Updated:** 2026-05-04  
**Maintained By:** Copilot CLI (Blaze.LlmGateway Squad)
