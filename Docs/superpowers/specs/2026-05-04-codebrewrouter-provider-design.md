# CodebrewRouterProvider — Standardized Provider Abstraction

**Date:** 2026-05-04  
**Author:** Copilot CLI (brainstorming + rubber-duck review)  
**Status:** Approved for implementation  
**Scope:** Refactor LocalInference Phase 1 services into a reusable, environment-agnostic provider standard suitable for desktop (Aspire), mobile (MAUI), and other .NET contexts.

---

## Executive Summary

The existing `LocalInference` Phase 1 implementation provides a rich feature set: local model availability tracking, remote discovery, health management, and hybrid routing. However, it's tightly coupled to Aspire and `IConfiguration`, making it unsuitable for mobile environments like MAUI.

**Goal:** Standardize these services as `CodebrewRouterProvider` — a fluent, composable DI abstraction that works seamlessly across all .NET environments.

**Key outcomes:**
- Single unified entry point: `services.AddCodebrewRouterProvider(options)`
- Optional fluent builder for advanced scenarios
- IOptions pattern (not IConfiguration) for mobile compatibility
- Full Phase 1 stack available: health checks, availability tracking, discovery, routing
- Backwards compatible with existing Aspire code

---

## Architecture Overview

### Component Model

```
┌─────────────────────────────────────────────────────────────┐
│  Application (Desktop / Mobile / Aspire)                    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  ICodebrewRouterProviderBuilder (fluent)                    │
│  ├─ AddCodebrewRouterProvider(options)                      │
│  ├─ .WithHealthChecks(configure?)                           │
│  ├─ .WithDiscovery(configure?)                              │
│  ├─ .WithRouting(configure?)                                │
│  ├─ .WithRoutingStrategy<T>(factory?)                       │
│  ├─ .WithLocalClient<T>(factory?)                           │
│  ├─ ValidateAsync() → ValidationResult                      │
│  └─ Build()                                                 │
└─────────────────────────────────────────────────────────────┘
                            │
                            ↓
┌─────────────────────────────────────────────────────────────┐
│  CodebrewRouterProvider (DI Registration)                   │
│                                                              │
│  Always Registered:                                         │
│  ├─ IOptions<CodebrewRouterProviderOptions>                │
│  ├─ IChatClient (unkeyed, routed)                          │
│  ├─ IHealthCheck (always, but can be disabled)             │
│                                                              │
│  Conditionally Registered (feature flags):                  │
│  ├─ ILocalModelAvailability (optional)                      │
│  ├─ ICodebrewRouterDiscoveryService (optional)             │
│  ├─ ILocalInferenceHealthManager (optional)                 │
│  ├─ IRoutingStrategy (swappable)                            │
│  └─ IChatClient keyed "LocalGemma" (optional)              │
└─────────────────────────────────────────────────────────────┘
```

### Three Deployment Modes

| Mode | Usage | DI Setup | Features |
|------|-------|----------|----------|
| **Mobile (MAUI)** | Minimal code | `services.AddCodebrewRouterProvider(opts)` | Core chat, availability, no remote discovery |
| **Desktop** | Full featured | `services.AddCodebrewRouterProvider(opts).WithDiscovery().WithRouting().Build()` | All Phase 1 services |
| **Aspire** | Orchestrated | Deprecation shim → new provider, OR direct builder | All Phase 1 + health aggregation |

---

## Design Decisions

### 1. Unified Entry Point + Optional Builder (Approach 2)

**Decision:** Single method signature with fluent builder chaining.

```csharp
// Both valid (mobile and desktop use same entry point)
services.AddCodebrewRouterProvider(options);

services.AddCodebrewRouterProvider(options)
    .WithHealthChecks()
    .WithDiscovery()
    .WithRouting()
    .Build();
```

**Rationale:**
- One API to learn across all platforms
- Mobile gets sensible defaults; desktop can opt-in to advanced features
- Builder is optional; calling `.Build()` is not required (implicit in DI finalization)

### 2. IOptions Pattern (No IConfiguration)

**Decision:** All configuration via `CodebrewRouterProviderOptions` value object, not `IConfiguration`.

```csharp
var options = new CodebrewRouterProviderOptions
{
    LocalEndpoint = "http://localhost:11434",
    RemoteDiscoveryEndpoint = "http://localhost:5273",
    CacheAvailabilityTtlSeconds = 60,
    DiscoveryPollingIntervalSeconds = 300,
    CircuitBreakerFailureThreshold = 5,
    CircuitBreakerCooldownMinutes = 5
};
services.AddCodebrewRouterProvider(options);
```

**Rationale:**
- Mobile doesn't have `appsettings.json`; explicit options work everywhere
- Testable without mocking `IConfiguration`
- Aspire apps can still populate options from config if desired (simple binding)

### 3. Health Check Registration (Always, Can Be Disabled)

**Decision:** `IHealthCheck` is always registered, but can be disabled via `HealthCheckOptions.Enabled = false`.

```csharp
services.AddCodebrewRouterProvider(options)
    .WithHealthChecks(cfg => cfg.Enabled = false)
    .Build();
```

Inside the health check: if disabled, return `HealthCheckResult(HealthStatus.Unhealthy, "Health check disabled")`.

**Rationale:**
- Aspire expects all health checks registered upfront (consistency with orchestration layer)
- Mobile can disable to reduce overhead
- Graceful degradation: absent health check doesn't break app startup

### 4. HttpClient as IHttpClientFactory (Not Singleton)

**Decision:** Use `IHttpClientFactory` for all HTTP operations (discovery service).

```csharp
services.AddHttpClient<CodebrewRouterDiscoveryService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(options.DiscoveryTimeoutSeconds ?? 30);
});
```

Discovery service injects `IHttpClientFactory`, creates clients per-request.

**Rationale:**
- Avoids port exhaustion on long-running processes
- Enables Aspire resilience policies (retry, circuit breaker)
- Follows standard .NET patterns

### 5. Initialization Order & Graceful Degradation

**Decision:** Health manager starts in `Degraded` state, bootstraps via explicit initialization.

- Replaces constructor-time subscription with explicit async initialization
- Builder provides hook for initialization after all services are registered
- First health check probes the availability services to populate state

**Rationale:**
- On mobile with no remote endpoint, doesn't immediately fail
- Allows services to initialize independently before orchestrating
- Clear separation: registration vs. initialization

### 6. Validation (Early, Actionable Feedback)

**Decision:** `Build()` runs synchronous validation; `ValidateAsync()` available for optional eager checks.

```csharp
// Mandatory (runs in Build())
- Endpoint format validation (valid URIs, not null)
- Required option presence (e.g., LocalEndpoint for availability tracking)

// Optional (in ValidateAsync())
- TCP reachability check on local endpoint
- HTTP connectivity check on remote discovery endpoint
```

**Rationale:**
- Mobile developers get fast feedback on misconfiguration
- Optional async validation for eager diagnostics
- Fails fast, not at request time

---

## Feature Flags & Configuration

### Builder Methods

| Method | Default | Feature | Notes |
|--------|---------|---------|-------|
| `.WithHealthChecks(cfg?)` | ✅ Enabled | Health state management | Always registered; can be disabled |
| `.WithDiscovery(cfg?)` | ✅ Enabled | Remote model discovery | HTTP polling with circuit breaker |
| `.WithRouting(cfg?)` | ✅ Enabled | Routing strategy | Hybrid local/remote or fallback |
| `.WithRoutingStrategy<T>(factory?)` | Hybrid | Replace strategy | Power users; must come after `.WithDiscovery()` |
| `.WithLocalClient<T>(factory?)` | LocalGemma | Replace local client | Custom inference backend |

### Configuration Options

```csharp
public class CodebrewRouterProviderOptions
{
    // Core (required)
    public string LocalEndpoint { get; set; } // "http://localhost:11434"
    
    // Availability tracking
    public int CacheAvailabilityTtlSeconds { get; set; } = 60;
    
    // Remote discovery (optional)
    public string? RemoteDiscoveryEndpoint { get; set; } // "http://localhost:5273"
    public int DiscoveryPollingIntervalSeconds { get; set; } = 300;
    public int DiscoveryTimeoutSeconds { get; set; } = 30;
    public int CircuitBreakerFailureThreshold { get; set; } = 5;
    public int CircuitBreakerCooldownMinutes { get; set; } = 5;
    
    // Health checks
    public bool HealthChecksEnabled { get; set; } = true;
    public int HealthCheckEventTimeoutSeconds { get; set; } = 300;
}
```

---

## Error Handling

### Exception Hierarchy

```csharp
public class CodebrewRouterProviderException : InvalidOperationException
{
    // Base for all provider errors
}

public class CodebrewRouterProviderValidationException : CodebrewRouterProviderException
{
    // Thrown in Build() if validation fails
    // Properties: ValidationErrors (List<ValidationError>)
}

public class CodebrewRouterProviderInitializationException : CodebrewRouterProviderException
{
    // Thrown during InitializeAsync() if services fail to bootstrap
}

public record ValidationError(string Code, string Message, string? Recommendation);
```

### Structured Logging

All Phase 1 services emit structured logs at critical junctures:
- Initialization: service startup, option binding
- Health transitions: state changes, reasons
- Discovery: polling start/stop, failures, circuit breaker events
- Routing: strategy selection, fallback activation
- Errors: validation failures, endpoint unreachability

---

## Testing Strategy

### Test Doubles

Provide factory methods for unit testing:

```csharp
// In test namespace
public static class CodebrewRouterProviderTesting
{
    // Create builder with sensible test defaults
    public static ICodebrewRouterProviderBuilder CreateForTesting(
        CodebrewRouterProviderOptions? options = null,
        IServiceCollection? services = null)
    {
        options ??= new CodebrewRouterProviderOptions 
        { 
            LocalEndpoint = "http://localhost:11434",
            HealthChecksEnabled = false  // Skip health checks in tests
        };
        services ??= new ServiceCollection();
        return services.AddCodebrewRouterProvider(options);
    }
    
    // Mock services
    public static TestLocalModelAvailability MockAvailability() => new();
    public static TestCodebrewRouterDiscoveryService MockDiscovery() => new();
}
```

### Testing Coverage Targets

- **Unit:** Provider builder logic, feature flag state tracking, validation rules
- **Integration:** Full stack registration, DI resolution, initialization sequence
- **E2E:** Mobile scenario (no remote discovery), Desktop scenario (full stack), Aspire scenario (health aggregation)

---

## Backwards Compatibility

### Deprecation Path

Existing `AddLocalInferenceServices(IConfiguration)` becomes a shim:

```csharp
[Obsolete("Use AddCodebrewRouterProvider(CodebrewRouterProviderOptions) instead. " +
    "This method will be removed in v2.0.", false)]
public static IServiceCollection AddLocalInferenceServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    var section = configuration.GetSection("LlmGateway:LocalInference");
    var options = new CodebrewRouterProviderOptions();
    section.Bind(options);
    
    services.AddCodebrewRouterProvider(options)
        .WithHealthChecks()
        .WithDiscovery()
        .WithRouting()
        .Build();
    
    return services;
}
```

**Migration guide:** Existing Aspire apps continue to work without changes. New apps use `CodebrewRouterProviderOptions` directly.

---

## Phase 2/3 Extensibility

### Future Hooks (Stubbed)

Builder includes pre-stubs for Phase 2/3 features:

```csharp
/// <summary>Phase 2: Add OpenTelemetry metrics, spans, and instrumentation.</summary>
/// <remarks>Not implemented yet.</remarks>
[Obsolete("Not implemented; planned for Phase 2", true)]
public ICodebrewRouterProviderBuilder WithOpenTelemetry(
    Action<OTelOptions>? configure = null) 
    => throw new NotImplementedException();

/// <summary>Phase 3: Enable Model Context Protocol (MCP) tool integration.</summary>
/// <remarks>Not implemented yet.</remarks>
[Obsolete("Not implemented; planned for Phase 3", true)]
public ICodebrewRouterProviderBuilder WithMcp(
    Action<McpOptions>? configure = null)
    => throw new NotImplementedException();
```

This signals where future phases will hook in without breaking the builder interface.

---

## Implementation Scope

### Files to Create/Modify

**New Files:**
- `Blaze.LlmGateway.Core/Provider/CodebrewRouterProviderOptions.cs`
- `Blaze.LlmGateway.Infrastructure/Provider/ICodebrewRouterProviderBuilder.cs`
- `Blaze.LlmGateway.Infrastructure/Provider/CodebrewRouterProviderBuilder.cs`
- `Blaze.LlmGateway.Infrastructure/Provider/ValidationResult.cs` and error types
- `Blaze.LlmGateway.Infrastructure/Provider/ServiceCollectionExtensions.cs` (new provider registration)
- `Blaze.LlmGateway.Tests/Provider/CodebrewRouterProviderTests.cs`
- `Blaze.LlmGateway.Tests/Provider/CodebrewRouterProviderBuilderTests.cs`

**Modified Files:**
- `Blaze.LlmGateway.LocalInference/ServiceCollectionExtensions.cs` (deprecation shim)
- `Blaze.LlmGateway.LocalInference/LocalInferenceHealthManager.cs` (initialization changes)
- `Program.cs` (Aspire app wiring, examples)

### Quality Gates

- ✅ Zero breaking changes to existing Phase 1 services (internal refactoring only)
- ✅ All existing tests continue to pass (137 tests)
- ✅ New provider tests achieve 95%+ coverage
- ✅ Backward compatibility shim works on existing Aspire apps
- ✅ Documentation updated with migration guide and three deployment examples

---

## Success Criteria

| Criterion | Validation |
|-----------|-----------|
| Mobile works | MAUI app can `services.AddCodebrewRouterProvider(opts)` and resolve `IChatClient` without Aspire |
| Desktop works | Desktop app can use builder with all Phase 1 features enabled |
| Aspire still works | Existing Aspire apps work unchanged via deprecation shim |
| Validation works | Misconfiguration caught in `Build()` with actionable error messages |
| Testing works | Unit tests pass, integration tests cover all three scenarios |
| Health manager graceful | Mobile with no remote endpoint doesn't immediately fail |
| HttpClient pattern | No resource leaks; IHttpClientFactory used throughout |

---

## Out of Scope (Phase 2+)

- OpenTelemetry metrics / instrumentation
- Exponential backoff for retries
- Configurable event timeout
- Adaptive health check intervals
- MCP integration
- Advanced strategy composition

---

## Rollout Plan

1. **Create** new provider abstraction (builder, options, validation)
2. **Refactor** Phase 1 services for initialization decoupling (health manager, discovery)
3. **Deprecate** old `AddLocalInferenceServices` with shim
4. **Test** across three scenarios: mobile, desktop, Aspire
5. **Document** migration guide and examples
6. **Commit** with backwards compat verified

---

## Appendix: Usage Examples

### Mobile (MAUI)

```csharp
// Program.cs in MAUI app
var options = new CodebrewRouterProviderOptions
{
    LocalEndpoint = "http://192.168.1.100:11434",  // Local Ollama on LAN
    RemoteDiscoveryEndpoint = null  // No remote discovery on mobile
};

builder.Services
    .AddCodebrewRouterProvider(options);

var app = builder.Build();
var chatClient = app.Services.GetRequiredService<IChatClient>();

// Use chatClient for chat completions
var response = await chatClient.GetResponseAsync([...]);
```

### Desktop

```csharp
// Program.cs in desktop app
var options = new CodebrewRouterProviderOptions
{
    LocalEndpoint = "http://localhost:11434",
    RemoteDiscoveryEndpoint = "http://localhost:5273"
};

builder.Services
    .AddCodebrewRouterProvider(options)
    .WithHealthChecks()
    .WithDiscovery()
    .WithRouting()
    .Build();

// Validate configuration early
await builder.Services.BuildServiceProvider()
    .GetRequiredService<ICodebrewRouterProviderBuilder>()
    .ValidateAsync();
```

### Aspire (Existing Code, No Change)

```csharp
// Program.cs in Aspire AppHost
var builder = DistributedApplication.CreateBuilder(args);

// Existing code continues to work via deprecation shim
builder.AddProject<Program>("api")
    .WithReference(builder.AddOllama("ollama"));

// Or migrate to new provider
var options = new CodebrewRouterProviderOptions { /* ... */ };
services.AddCodebrewRouterProvider(options).Build();
```

---

**End of Design Document**
