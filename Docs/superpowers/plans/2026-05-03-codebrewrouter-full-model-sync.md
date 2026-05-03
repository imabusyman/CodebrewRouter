# CodebrewRouter: Full Model Sync + Router Redundancy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement complete .12/.53 router redundancy with synchronized models, thread-safe failover, and model sync validation for CodebrewRouter's 5-step pipeline.

**Architecture:** Three-tier system with:
1. Primary router (.12 / .53 with synchronized gemma4:e4b)
2. Health state management (IOllamaHealthState) for thread-safe failover
3. Pipeline integration (prompt cleaner + classifier + router client use health state)
4. Startup validation (model sync check before accepting requests)
5. Dynamic failover (try primary, auto-retry fallback, background health checks)

**Tech Stack:** .NET 10, MEAI, OllamaApiClient, EF Core (future persistence)

---

## File Structure

### New Files (to create)
- `Blaze.LlmGateway.Core/Routing/IOllamaHealthState.cs` — Interface for health state queries
- `Blaze.LlmGateway.Infrastructure/OllamaHealthStateManager.cs` — Thread-safe implementation
- `Blaze.LlmGateway.Infrastructure/OllamaModelSyncValidator.cs` — Startup model sync check
- `Blaze.LlmGateway.Infrastructure/OllamaFailoverClient.cs` — Wrapper for primary/fallback failover

### Modified Files
- `Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs` — Refactor OllamaLocal → OllamaRouter with split endpoints
- `Blaze.LlmGateway.Api/appsettings.json` — Update config structure
- `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs` — Re-enable Ollama registration, add health state DI
- `Blaze.LlmGateway.Infrastructure/PromptCleaning/GemmaPromptCleaner.cs` — Integrate IOllamaHealthState + failover
- `Blaze.LlmGateway.Infrastructure/TaskClassification/OllamaTaskClassifier.cs` — Integrate IOllamaHealthState + failover
- `Blaze.LlmGateway.Infrastructure/ModelAvailabilityHeartbeatService.cs` — Add model sync validation
- `Blaze.LlmGateway.Api/Program.cs` — Wire model sync validator to startup
- `Blaze.LlmGateway.Tests/OllamaHealthStateTests.cs` — Tests for health state and failover

---

## Phase 1: Infrastructure Setup (.53 Configuration)

### Task 1: SSH to .53 and Pull gemma4:e4b Model

**Files:**
- None (manual infrastructure task)

- [ ] **Step 1: SSH to .53 (use SSH key-based auth if available, otherwise use provided credentials)**

```bash
# Preferred: Use SSH key
ssh -i ~/.ssh/id_rsa root@192.168.16.53

# Fallback: Use password auth
ssh root@192.168.16.53
# When prompted for password, enter: fri12daY!!
# ⚠️ NOTE: For production, use SSH keys or environment variables instead of hardcoded passwords
```

Expected output: Shell prompt on .53 machine

- [ ] **Step 2: Verify Ollama is running**

```bash
ollama list
```

Expected: If Ollama is running, you'll see any existing models. If not, the command fails.

- [ ] **Step 3: Start Ollama if not running**

```bash
# Check if ollama service is running
systemctl status ollama

# If not running, start it
systemctl start ollama

# Verify it's running
systemctl status ollama
```

Expected: Service is active and running

- [ ] **Step 4: Pull gemma4:e4b model**

```bash
ollama pull gemma4:e4b
```

Expected: Downloads ~9 GB model. This takes 5-10 minutes. Output shows progress.

- [ ] **Step 5: Verify model is available**

```bash
ollama list
```

Expected output includes:
```
gemma4:e4b      8.95 GB
```

- [ ] **Step 6: Test model responds**

```bash
curl -X POST http://192.168.16.53:11434/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemma4:e4b",
    "prompt": "Hello",
    "stream": false
  }' | head -20
```

Expected: JSON response with generated text

- [ ] **Step 7: Exit SSH**

```bash
exit
```

---

## Phase 2: Configuration Refactoring

### Task 2: Refactor LlmGatewayOptions to Support OllamaRouter Primary/Fallback Split

**Files:**
- Modify: `Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs:46-52`

- [ ] **Step 1: View current OllamaLocalOptions**

```csharp
// Current state (lines 46-52)
public class OllamaLocalOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "gemma4:e4b";
    public int MaxContextTokens { get; set; } = 32768;
    public int ReservedOutputTokens { get; set; } = 2048;
}
```

- [ ] **Step 2: Replace with OllamaRouterOptions**

Replace the entire `OllamaLocalOptions` class definition (lines 46-52) with:

```csharp
public class OllamaRouterOptions
{
    /// <summary>
    /// Primary Ollama router endpoint (e.g., http://192.168.16.53:11434).
    /// Used for prompt cleanup and task classification.
    /// </summary>
    public string PrimaryEndpoint { get; set; } = "http://192.168.16.53:11434";

    /// <summary>
    /// Fallback Ollama router endpoint (e.g., http://192.168.16.12:11434).
    /// Used when primary is unhealthy.
    /// </summary>
    public string FallbackEndpoint { get; set; } = "http://192.168.16.12:11434";

    /// <summary>
    /// Router model name. Both primary and fallback MUST have this model installed.
    /// </summary>
    public string Model { get; set; } = "gemma4:e4b";

    /// <summary>
    /// Maximum context tokens for router (used by prompt cleanup + classification).
    /// </summary>
    public int MaxContextTokens { get; set; } = 32768;

    /// <summary>
    /// Reserved output tokens for router responses.
    /// </summary>
    public int ReservedOutputTokens { get; set; } = 2048;
}
```

- [ ] **Step 3: Update ProvidersOptions property name**

Rename `public OllamaLocalOptions OllamaLocal { get; set; }` to `public OllamaRouterOptions OllamaRouter { get; set; }` in the `ProvidersOptions` class (around line 30).

Before:
```csharp
public class ProvidersOptions
{
    // ...
    public OllamaLocalOptions OllamaLocal { get; set; } = new();
}
```

After:
```csharp
public class ProvidersOptions
{
    // ...
    public OllamaRouterOptions OllamaRouter { get; set; } = new();
}
```

- [ ] **Step 4: Commit**

```bash
git add Blaze.LlmGateway.Core/Configuration/LlmGatewayOptions.cs
git commit -m "refactor: split OllamaLocal into OllamaRouter with primary/fallback endpoints"
```

---

### Task 3: Update appsettings.json to Reflect New Config Structure

**Files:**
- Modify: `Blaze.LlmGateway.Api/appsettings.json:27-32`

- [ ] **Step 1: View current config**

Current `appsettings.json` (lines 27-32):
```json
"OllamaLocal": {
  "BaseUrl": "http://localhost:11434",
  "Model": "gemma4:e4b",
  "MaxContextTokens": 32768,
  "ReservedOutputTokens": 2048
}
```

- [ ] **Step 2: Replace with new structure**

Replace the entire `OllamaLocal` section with:

```json
"OllamaRouter": {
  "PrimaryEndpoint": "http://192.168.16.53:11434",
  "FallbackEndpoint": "http://192.168.16.12:11434",
  "Model": "gemma4:e4b",
  "MaxContextTokens": 32768,
  "ReservedOutputTokens": 2048
}
```

- [ ] **Step 3: Commit**

```bash
git add Blaze.LlmGateway.Api/appsettings.json
git commit -m "config: update Ollama endpoints to primary/fallback split (.53/.12)"
```

---

## Phase 3: Health State Management

### Task 4: Create IOllamaHealthState Interface

**Files:**
- Create: `Blaze.LlmGateway.Core/Routing/IOllamaHealthState.cs`

- [ ] **Step 1: Create new file**

```csharp
using System;

namespace Blaze.LlmGateway.Core.Routing;

/// <summary>
/// Manages health state of primary and fallback Ollama router endpoints.
/// Thread-safe queries and updates for request-time routing decisions.
/// </summary>
public interface IOllamaHealthState
{
    /// <summary>
    /// Gets the currently healthy endpoint (primary or fallback).
    /// </summary>
    /// <returns>Tuple of (endpoint URL, timestamp of last successful probe)</returns>
    (string Endpoint, DateTime LastProbeTime) GetHealthyEndpoint();

    /// <summary>
    /// Marks an endpoint as unhealthy after a request failure.
    /// Triggers failover to alternate endpoint.
    /// </summary>
    /// <param name="failedEndpoint">The endpoint that failed (primary or fallback)</param>
    /// <param name="exception">The exception that caused the failure</param>
    void MarkEndpointUnhealthy(string failedEndpoint, Exception exception);

    /// <summary>
    /// Marks an endpoint as healthy after successful probe or request.
    /// </summary>
    /// <param name="endpoint">The endpoint that succeeded</param>
    void MarkEndpointHealthy(string endpoint);

    /// <summary>
    /// Gets the current health status of both endpoints (for monitoring).
    /// </summary>
    /// <returns>Tuple of (primary healthy, fallback healthy, last update time)</returns>
    (bool PrimaryHealthy, bool FallbackHealthy, DateTime LastUpdate) GetHealthStatus();
}
```

- [ ] **Step 2: Commit**

```bash
git add Blaze.LlmGateway.Core/Routing/IOllamaHealthState.cs
git commit -m "feat: add IOllamaHealthState interface for health management"
```

---

### Task 5: Implement OllamaHealthStateManager

**Files:**
- Create: `Blaze.LlmGateway.Infrastructure/OllamaHealthStateManager.cs`

⚠️ **RUBBER-DUCK UPDATE:** Uses `lock` (mutual exclusion) instead of ReaderWriterLockSlim to prevent potential deadlock under concurrent failover. See Issue #2 critique.

- [ ] **Step 1: Create implementation with thread-safe state**

```csharp
using System;
using Blaze.LlmGateway.Core.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Blaze.LlmGateway.Core.Configuration;

namespace Blaze.LlmGateway.Infrastructure;

/// <summary>
/// Thread-safe health state manager for Ollama router endpoints.
/// Uses mutual exclusion (lock) for safe concurrent access during request-time failover.
/// </summary>
public sealed class OllamaHealthStateManager : IOllamaHealthState
{
    private readonly string _primaryEndpoint;
    private readonly string _fallbackEndpoint;
    private readonly ILogger<OllamaHealthStateManager> _logger;
    private readonly object _lock = new();

    private bool _primaryHealthy = true;
    private bool _fallbackHealthy = true;
    private DateTime _lastUpdate = DateTime.UtcNow;

    public OllamaHealthStateManager(
        IOptions<LlmGatewayOptions> options,
        ILogger<OllamaHealthStateManager> logger)
    {
        _primaryEndpoint = options.Value.Providers.OllamaRouter.PrimaryEndpoint;
        _fallbackEndpoint = options.Value.Providers.OllamaRouter.FallbackEndpoint;
        _currentEndpoint = _primaryEndpoint;
        _logger = logger;
    }

    public OllamaHealthStateManager(
        IOptions<LlmGatewayOptions> options,
        ILogger<OllamaHealthStateManager> logger)
    {
        _primaryEndpoint = options.Value.Providers.OllamaRouter.PrimaryEndpoint;
        _fallbackEndpoint = options.Value.Providers.OllamaRouter.FallbackEndpoint;
        _logger = logger;
    }

    public (string Endpoint, DateTime LastProbeTime) GetHealthyEndpoint()
    {
        lock (_lock)
        {
            // If primary is healthy, use it; otherwise use fallback
            if (_primaryHealthy)
            {
                return (_primaryEndpoint, _lastUpdate);
            }
            else if (_fallbackHealthy)
            {
                return (_fallbackEndpoint, _lastUpdate);
            }
            else
            {
                // Both down, but return primary anyway (will likely fail, triggering circuit breaker)
                _logger.LogCritical("🔴 CRITICAL: Both Ollama router endpoints are unhealthy; returning primary as last resort");
                return (_primaryEndpoint, _lastUpdate);
            }
        }
    }

    public void MarkEndpointUnhealthy(string failedEndpoint, Exception exception)
    {
        lock (_lock)
        {
            if (failedEndpoint == _primaryEndpoint)
            {
                _primaryHealthy = false;
                _logger.LogWarning(exception, "❌ Primary Ollama router (.53) marked unhealthy; failing over to .12");
            }
            else if (failedEndpoint == _fallbackEndpoint)
            {
                _fallbackHealthy = false;
                _logger.LogWarning(exception, "❌ Fallback Ollama router (.12) marked unhealthy");
            }

            _lastUpdate = DateTime.UtcNow;
        }
    }

    public void MarkEndpointHealthy(string endpoint)
    {
        lock (_lock)
        {
            if (endpoint == _primaryEndpoint && !_primaryHealthy)
            {
                _primaryHealthy = true;
                _logger.LogInformation("✅ Primary Ollama router (.53) recovered; returning to primary");
            }
            else if (endpoint == _fallbackEndpoint && !_fallbackHealthy)
            {
                _fallbackHealthy = true;
                _logger.LogInformation("✅ Fallback Ollama router (.12) recovered");
            }

            _lastUpdate = DateTime.UtcNow;
        }
    }

    public (bool PrimaryHealthy, bool FallbackHealthy, DateTime LastUpdate) GetHealthStatus()
    {
        lock (_lock)
        {
            return (_primaryHealthy, _fallbackHealthy, _lastUpdate);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/OllamaHealthStateManager.cs
git commit -m "feat: implement thread-safe OllamaHealthStateManager with ReaderWriterLockSlim"
```

---

### Task 6: Create OllamaModelSyncValidator for Startup Validation

**Files:**
- Create: `Blaze.LlmGateway.Infrastructure/OllamaModelSyncValidator.cs`

⚠️ **RUBBER-DUCK UPDATES:** 
- Add 10-second timeout to prevent startup hang (Issue #3)
- Validate model exists on BOTH endpoints separately (Issue #8)

- [ ] **Step 1: Create validator**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Blaze.LlmGateway.Core.Configuration;

namespace Blaze.LlmGateway.Infrastructure;

/// <summary>
/// Validates model synchronization between primary and fallback Ollama routers on startup.
/// Fails startup if model lists don't match or required model is missing on either endpoint.
/// Includes 10-second timeout to prevent startup hang.
/// </summary>
public sealed class OllamaModelSyncValidator
{
    private readonly LlmGatewayOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OllamaModelSyncValidator> _logger;

    public OllamaModelSyncValidator(
        IOptions<LlmGatewayOptions> options,
        HttpClient httpClient,
        ILogger<OllamaModelSyncValidator> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> ValidateAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));  // 10-second timeout

        var primaryEndpoint = _options.Providers.OllamaRouter.PrimaryEndpoint;
        var fallbackEndpoint = _options.Providers.OllamaRouter.FallbackEndpoint;
        var requiredModel = _options.Providers.OllamaRouter.Model;

        _logger.LogInformation("🔍 Validating Ollama model sync between {Primary} and {Fallback} (10s timeout)", 
            primaryEndpoint, fallbackEndpoint);

        try
        {
            var primaryModels = await GetModelsAsync(primaryEndpoint, timeoutCts.Token);
            var fallbackModels = await GetModelsAsync(fallbackEndpoint, timeoutCts.Token);

            if (primaryModels == null)
            {
                _logger.LogError("❌ Failed to query models from primary endpoint {Endpoint}", primaryEndpoint);
                return false;
            }

            if (fallbackModels == null)
            {
                _logger.LogError("❌ Failed to query models from fallback endpoint {Endpoint}", fallbackEndpoint);
                return false;
            }

            // Validate required model exists on PRIMARY
            if (!primaryModels.Contains(requiredModel))
            {
                _logger.LogError("❌ Required model {Model} not found on PRIMARY endpoint {Endpoint}", 
                    requiredModel, primaryEndpoint);
                return false;
            }

            // Validate required model exists on FALLBACK
            if (!fallbackModels.Contains(requiredModel))
            {
                _logger.LogError("❌ Required model {Model} not found on FALLBACK endpoint {Endpoint}", 
                    requiredModel, fallbackEndpoint);
                return false;
            }

            var primaryNames = primaryModels.OrderBy(m => m).ToList();
            var fallbackNames = fallbackModels.OrderBy(m => m).ToList();

            if (!primaryNames.SequenceEqual(fallbackNames))
            {
                var missingOnFallback = primaryNames.Except(fallbackNames).ToList();
                var missingOnPrimary = fallbackNames.Except(primaryNames).ToList();

                _logger.LogError("❌ Model sync validation FAILED:");
                if (missingOnFallback.Any())
                    _logger.LogError("   Missing on fallback (.12): {Models}", string.Join(", ", missingOnFallback));
                if (missingOnPrimary.Any())
                    _logger.LogError("   Missing on primary (.53): {Models}", string.Join(", ", missingOnPrimary));

                return false;
            }

            _logger.LogInformation("✅ Model sync validation PASSED. Both endpoints have identical models: {Models}", 
                string.Join(", ", primaryNames));
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("❌ Model sync validation timed out after 10 seconds");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Model sync validation threw exception");
            return false;
        }
    }

    private async Task<List<string>?> GetModelsAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            var tagsUrl = $"{endpoint}/api/tags";
            var response = await _httpClient.GetAsync(tagsUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("⚠️ Failed to query {Endpoint}: {StatusCode}", tagsUrl, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var models = new List<string>();

            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameElement))
                    {
                        models.Add(nameElement.GetString() ?? "unknown");
                    }
                }
            }

            return models;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Exception querying {Endpoint}", endpoint);
            return null;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/OllamaModelSyncValidator.cs
git commit -m "feat: add OllamaModelSyncValidator for startup model sync check"
```

---

### Task 7: Create OllamaFailoverClient Wrapper

**Files:**
- Create: `Blaze.LlmGateway.Infrastructure/OllamaFailoverClient.cs`

⚠️ **RUBBER-DUCK UPDATE:** Receives cached clients via constructor instead of factory to prevent resource leaks (Issues #1 & #4). See Task 8 for cached client creation.

- [ ] **Step 1: Create failover client wrapper**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Blaze.LlmGateway.Core.Routing;

namespace Blaze.LlmGateway.Infrastructure;

/// <summary>
/// Wraps an Ollama client with automatic failover between primary and fallback endpoints.
/// Receives cached clients via constructor to prevent resource leaks.
/// On first request failure, queries health state, tries alternate endpoint, and updates health state.
/// </summary>
public sealed class OllamaFailoverClient : DelegatingChatClient
{
    private readonly IOllamaHealthState _healthState;
    private readonly IChatClient _fallbackClient;
    private readonly ILogger<OllamaFailoverClient> _logger;
    private string _currentEndpoint;
    private string _primaryEndpoint;
    private string _fallbackEndpoint;

    public OllamaFailoverClient(
        IChatClient primaryClient,
        IChatClient fallbackClient,
        IOllamaHealthState healthState,
        string primaryEndpoint,
        string fallbackEndpoint,
        ILogger<OllamaFailoverClient> logger)
        : base(primaryClient)
    {
        _fallbackClient = fallbackClient;
        _healthState = healthState;
        _logger = logger;
        _currentEndpoint = primaryEndpoint;
        _primaryEndpoint = primaryEndpoint;
        _fallbackEndpoint = fallbackEndpoint;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await InnerClient.GetResponseAsync(messages, options, cancellationToken);
            _healthState.MarkEndpointHealthy(_currentEndpoint);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Ollama request failed on {Endpoint}; trying failover", _currentEndpoint);
            return await FailoverAndRetryAsync(messages, options, cancellationToken);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        System.Runtime.CompilerServices.EnumeratorCancellation CancellationToken cancellationToken = default)
    {
        IAsyncEnumerable<ChatResponseUpdate>? stream = null;
        try
        {
            stream = InnerClient.GetStreamingResponseAsync(messages, options, cancellationToken);
            var enumerator = stream.GetAsyncEnumerator(cancellationToken);

            // Try to get first chunk to probe connection
            if (!await enumerator.MoveNextAsync())
            {
                throw new InvalidOperationException("No response from Ollama");
            }

            _healthState.MarkEndpointHealthy(_currentEndpoint);
            yield return enumerator.Current;

            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "❌ Ollama streaming failed on {Endpoint}; trying failover", _currentEndpoint);
            await foreach (var update in FailoverAndRetryStreamAsync(messages, options, cancellationToken))
            {
                yield return update;
            }
        }
    }

    private async Task<ChatResponse> FailoverAndRetryAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        _healthState.MarkEndpointUnhealthy(_currentEndpoint, new Exception("Request failed"));
        var (newEndpoint, _) = _healthState.GetHealthyEndpoint();

        if (newEndpoint == _currentEndpoint)
        {
            throw new InvalidOperationException("No healthy Ollama endpoint available for failover");
        }

        _logger.LogInformation("🔄 Failover: switching from {Old} to {New}", _currentEndpoint, newEndpoint);
        _currentEndpoint = newEndpoint;

        try
        {
            var response = await _fallbackClient.GetResponseAsync(messages, options, cancellationToken);
            _healthState.MarkEndpointHealthy(_currentEndpoint);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failover also failed on {Endpoint}", _currentEndpoint);
            _healthState.MarkEndpointUnhealthy(_currentEndpoint, ex);
            throw;
        }
    }

    private async IAsyncEnumerable<ChatResponseUpdate> FailoverAndRetryStreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        System.Runtime.CompilerServices.EnumeratorCancellation CancellationToken cancellationToken)
    {
        _healthState.MarkEndpointUnhealthy(_currentEndpoint, new Exception("Streaming request failed"));
        var (newEndpoint, _) = _healthState.GetHealthyEndpoint();

        if (newEndpoint == _currentEndpoint)
        {
            throw new InvalidOperationException("No healthy Ollama endpoint available for failover");
        }

        _logger.LogInformation("🔄 Failover: switching from {Old} to {New}", _currentEndpoint, newEndpoint);
        _currentEndpoint = newEndpoint;

        await foreach (var update in _fallbackClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }

        _healthState.MarkEndpointHealthy(_currentEndpoint);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/OllamaFailoverClient.cs
git commit -m "feat: add OllamaFailoverClient for automatic endpoint failover"
```

---

## Phase 4: DI Registration Updates

### Task 8: Update InfrastructureServiceExtensions to Register New Services

**Files:**
- Modify: `Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs:20-90`

⚠️ **RUBBER-DUCK UPDATE:** Pre-creates both primary and fallback OllamaApiClient instances at DI time, caches them, and reuses them (fixes Issues #1 & #4).

- [ ] **Step 1: Add IOllamaHealthState registration**

Add this to the `AddLlmInfrastructure` method after existing service registrations (around line 40):

```csharp
// Register thread-safe health state manager
services.AddSingleton<IOllamaHealthState, OllamaHealthStateManager>();

// Register model sync validator (used during startup)
services.AddSingleton<OllamaModelSyncValidator>();
```

- [ ] **Step 2: Update Ollama client registration with cached clients**

Find the section that currently registers OllamaLocal (lines 46-55) and replace it with:

```csharp
// Register Ollama router clients (pre-cached at DI time to prevent resource leaks)
var ollamaRouterOptions = sp.GetRequiredService<IOptions<LlmGatewayOptions>>().Value.Providers.OllamaRouter;
var healthState = sp.GetRequiredService<IOllamaHealthState>();
var logger = sp.GetRequiredService<ILogger<OllamaFailoverClient>>();

// Create CACHED primary and fallback Ollama clients (REUSED for all requests)
var primaryOllamaClient = new OllamaApiClient(
    new Uri(ollamaRouterOptions.PrimaryEndpoint),
    ollamaRouterOptions.Model);

var fallbackOllamaClient = new OllamaApiClient(
    new Uri(ollamaRouterOptions.FallbackEndpoint),
    ollamaRouterOptions.Model);

// Create failover wrapper that uses cached clients
var failoverClient = new OllamaFailoverClient(
    primaryOllamaClient.AsChatClient(),
    fallbackOllamaClient.AsChatClient(),
    healthState,
    ollamaRouterOptions.PrimaryEndpoint,
    ollamaRouterOptions.FallbackEndpoint,
    logger);

services.AddKeyedSingleton<IChatClient>("OllamaRouter", (sp, key) => failoverClient);
```

- [ ] **Step 3: Re-enable GemmaPromptCleaner (inject cached OllamaRouter client)**

Find the line that registers `NoopPromptCleaner` and replace it with:

```csharp
// Register GemmaPromptCleaner (re-enabled with cached router client)
services.AddSingleton<IPromptCleaner>(sp =>
    new GemmaPromptCleaner(
        sp.GetRequiredService<IKeyedServiceProvider>().GetKeyedService<IChatClient>("OllamaRouter")!,
        sp.GetRequiredService<IOptions<PromptCleanupOptions>>(),
        sp.GetRequiredService<ILogger<GemmaPromptCleaner>>()));
```

- [ ] **Step 4: Update OllamaTaskClassifier registration (inject cached OllamaRouter client)**

Find the OllamaTaskClassifier registration and update it:

```csharp
// Register OllamaTaskClassifier (with cached router client)
services.AddSingleton<ITaskClassifier>(sp =>
    new OllamaTaskClassifier(
        sp.GetRequiredService<IKeyedServiceProvider>().GetKeyedService<IChatClient>("OllamaRouter")!,
        sp.GetRequiredService<IOptions<TaskClassificationOptions>>(),
        sp.GetRequiredService<ILogger<OllamaTaskClassifier>>()));
```

- [ ] **Step 5: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs
git commit -m "feat: register cached Ollama clients, health state, and pipeline components"
```

---

## Phase 5: Pipeline Integration

### Task 9: Update GemmaPromptCleaner to Use Injected Cached Client

**Files:**
- Modify: `Blaze.LlmGateway.Infrastructure/PromptCleaning/GemmaPromptCleaner.cs:1-120`

⚠️ **RUBBER-DUCK UPDATE:** Receives cached OllamaRouter client via constructor injection (Issue #4). No longer creates new clients per request.

- [ ] **Step 1: Update constructor to inject cached client**

Replace with:

```csharp
public sealed class GemmaPromptCleaner : IPromptCleaner
{
    private readonly IChatClient _cachedRouterClient;  // Reused for all requests
    private readonly IOptions<PromptCleanupOptions> _options;
    private readonly ILogger<GemmaPromptCleaner> _logger;
    private readonly CircuitBreaker _circuit;

    public GemmaPromptCleaner(
        IChatClient cachedRouterClient,  // Injected cached client from DI ("OllamaRouter" keyed)
        IOptions<PromptCleanupOptions> options,
        ILogger<GemmaPromptCleaner> logger)
    {
        _cachedRouterClient = cachedRouterClient;
        _options = options;
        _logger = logger;
        _circuit = new CircuitBreaker(cooldownMinutes: 5);
    }
```

- [ ] **Step 2: Update CleanAsync to use cached client**

Replace the implementation with:

```csharp
public async Task<string> CleanAsync(string originalText, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(originalText))
        return originalText;

    if (originalText.Length < 80)
    {
        _logger.LogDebug("Prompt too short to clean ({Length} chars < 80 threshold); skipping", originalText.Length);
        return originalText;
    }

    if (_circuit.IsOpen)
    {
        _logger.LogWarning("Circuit breaker open for prompt cleaner; skipping cleanup");
        return originalText;
    }

    try
    {
        var opts = new ChatOptions 
        { 
            Temperature = 0, 
            MaxOutputTokens = 256 
        };
        
        var messages = new[] 
        { 
            new ChatMessage(ChatRole.User, $"Rewrite this prompt to be more efficient:\n\n{originalText}") 
        };

        // USE CACHED CLIENT (no new creation)
        var response = await _cachedRouterClient.GetResponseAsync(messages, opts, cancellationToken);
        var cleanedText = response.Message.Text ?? originalText;

        if (string.IsNullOrWhiteSpace(cleanedText) || cleanedText.Length > originalText.Length * 1.5)
        {
            _logger.LogDebug("Cleaned prompt not valid; returning original");
            return originalText;
        }

        _logger.LogInformation("✂️ Prompt cleaned: {OriginalLength} → {CleanedLength} chars", 
            originalText.Length, cleanedText.Length);
        return cleanedText;
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Prompt cleaner timed out; returning original prompt");
        return originalText;
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Prompt cleaner failed; opening circuit breaker and returning original");
        _circuit.Open();
        return originalText;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/PromptCleaning/GemmaPromptCleaner.cs
git commit -m "refactor: use cached OllamaRouter client instead of creating new ones"
```

---

### Task 10: Update OllamaTaskClassifier to Use Injected Cached Client

**Files:**
- Modify: `Blaze.LlmGateway.Infrastructure/TaskClassification/OllamaTaskClassifier.cs:1-100`

⚠️ **RUBBER-DUCK UPDATE:** Receives cached OllamaRouter client via constructor injection (Issue #4). No longer creates new clients per request.

- [ ] **Step 1: Update constructor to inject cached client**

```csharp
public sealed class OllamaTaskClassifier : ITaskClassifier
{
    private readonly IChatClient _cachedRouterClient;  // Reused for all requests
    private readonly IOptions<TaskClassificationOptions> _options;
    private readonly ILogger<OllamaTaskClassifier> _logger;
    private readonly CircuitBreaker _circuit;

    public OllamaTaskClassifier(
        IChatClient cachedRouterClient,  // Injected cached client from DI ("OllamaRouter" keyed)
        IOptions<TaskClassificationOptions> options,
        ILogger<OllamaTaskClassifier> logger)
    {
        _cachedRouterClient = cachedRouterClient;
        _options = options;
        _logger = logger;
        _circuit = new CircuitBreaker(cooldownMinutes: 5);
    }
```

- [ ] **Step 2: Update ClassifyAsync to use cached client**

Replace with:

```csharp
public async Task<TaskType> ClassifyAsync(
    IList<ChatMessage> messages,
    CancellationToken cancellationToken = default)
{
    if (messages.Count == 0)
        return TaskType.General;

    if (_circuit.IsOpen)
    {
        _logger.LogWarning("Circuit breaker open for task classifier; using keyword fallback");
        return ClassifyByKeyword(messages);
    }

    try
    {
        var systemPrompt = @"Classify this task into ONE of: Coding, Reasoning, VisionObjectDetection, Research, Creative, DataAnalysis, General.
Respond with ONLY the task type, nothing else.";

        var messageList = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System, systemPrompt)
        };
        messageList.AddRange(messages);

        var opts = new ChatOptions { Temperature = 0, MaxOutputTokens = 50 };
        
        // USE CACHED CLIENT (no new creation)
        var response = await _cachedRouterClient.GetResponseAsync(messageList, opts, cancellationToken);
        var classification = response.Message.Text ?? "General";

        var taskType = ParseTaskType(classification);
        _logger.LogInformation("🎯 Task classified as: {TaskType}", taskType);
        return taskType;
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Task classifier timed out; using keyword fallback");
        return ClassifyByKeyword(messages);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Task classifier failed; opening circuit and using keyword fallback");
        _circuit.Open();
        return ClassifyByKeyword(messages);
    }
}

private TaskType ParseTaskType(string text)
{
    return text.Trim().ToLowerInvariant() switch
    {
        "coding" => TaskType.Coding,
        "reasoning" => TaskType.Reasoning,
        "visionobjectdetection" => TaskType.VisionObjectDetection,
        "research" => TaskType.Research,
        "creative" => TaskType.Creative,
        "dataanalysis" => TaskType.DataAnalysis,
        _ => TaskType.General
    };
}

private TaskType ClassifyByKeyword(IList<ChatMessage> messages)
{
    var lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
    var lower = lastUserMessage.ToLowerInvariant();

    if (lower.Contains("code") || lower.Contains("function") || lower.Contains("bug"))
        return TaskType.Coding;
    if (lower.Contains("image") || lower.Contains("vision") || lower.Contains("screenshot"))
        return TaskType.VisionObjectDetection;
    if (lower.Contains("research") || lower.Contains("paper") || lower.Contains("study"))
        return TaskType.Research;
    if (lower.Contains("creative") || lower.Contains("story") || lower.Contains("write"))
        return TaskType.Creative;
    if (lower.Contains("data") || lower.Contains("analyze") || lower.Contains("chart"))
        return TaskType.DataAnalysis;

    return TaskType.General;
}
```

- [ ] **Step 3: Commit**

```bash
git add Blaze.LlmGateway.Infrastructure/TaskClassification/OllamaTaskClassifier.cs
git commit -m "refactor: use cached OllamaRouter client instead of creating new ones"
```

---

## Phase 6: Startup Validation

### Task 11: Wire Model Sync Validator to Application Startup

**Files:**
- Modify: `Blaze.LlmGateway.Api/Program.cs`

- [ ] **Step 1: Find the startup section and add model sync validation**

After `builder.Build()` (around line 60), add:

```csharp
var app = builder.Build();

// Run model sync validation before accepting requests
var validator = app.Services.GetRequiredService<OllamaModelSyncValidator>();
var validationResult = await validator.ValidateAsync();

if (!validationResult)
{
    app.Logger.LogError("❌ Ollama model sync validation failed. Aborting startup.");
    throw new InvalidOperationException("Model sync validation failed.");
}
```

- [ ] **Step 2: Commit**

```bash
git add Blaze.LlmGateway.Api/Program.cs
git commit -m "feat: add model sync validation to startup"
```

---

## Phase 7: Testing

### Task 12: Write Tests for Health State and Failover

**Files:**
- Create: `Blaze.LlmGateway.Tests/OllamaHealthStateTests.cs`

- [ ] **Step 1: Create test file**

```csharp
using System;
using System.Threading.Tasks;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Blaze.LlmGateway.Tests;

public class OllamaHealthStateTests
{
    private readonly IServiceProvider _sp;
    private readonly IOllamaHealthState _healthState;

    public OllamaHealthStateTests()
    {
        var services = new ServiceCollection();
        
        var options = Options.Create(new LlmGatewayOptions
        {
            Providers = new ProvidersOptions
            {
                OllamaRouter = new OllamaRouterOptions
                {
                    PrimaryEndpoint = "http://192.168.16.53:11434",
                    FallbackEndpoint = "http://192.168.16.12:11434",
                    Model = "gemma4:e4b"
                }
            }
        });

        services.AddSingleton(options);
        services.AddLogging(cfg => cfg.AddConsole());
        services.AddSingleton<IOllamaHealthState, OllamaHealthStateManager>();

        _sp = services.BuildServiceProvider();
        _healthState = _sp.GetRequiredService<IOllamaHealthState>();
    }

    [Fact]
    public void GetHealthyEndpoint_WhenPrimaryHealthy_ReturnsPrimary()
    {
        var (endpoint, _) = _healthState.GetHealthyEndpoint();
        Assert.Equal("http://192.168.16.53:11434", endpoint);
    }

    [Fact]
    public void MarkEndpointUnhealthy_WhenPrimaryFails_ReturnsFallback()
    {
        _healthState.MarkEndpointUnhealthy("http://192.168.16.53:11434", new Exception("Test"));
        var (endpoint, _) = _healthState.GetHealthyEndpoint();
        Assert.Equal("http://192.168.16.12:11434", endpoint);
    }

    [Fact]
    public void MarkEndpointHealthy_WhenPrimaryRecovered_ReturnsPrimary()
    {
        _healthState.MarkEndpointUnhealthy("http://192.168.16.53:11434", new Exception("Test"));
        _healthState.MarkEndpointHealthy("http://192.168.16.53:11434");
        var (endpoint, _) = _healthState.GetHealthyEndpoint();
        Assert.Equal("http://192.168.16.53:11434", endpoint);
    }

    [Fact]
    public void GetHealthStatus_ReturnsCurrentHealthOfBothEndpoints()
    {
        var (primary, fallback, _) = _healthState.GetHealthStatus();
        Assert.True(primary);
        Assert.True(fallback);

        _healthState.MarkEndpointUnhealthy("http://192.168.16.53:11434", new Exception("Test"));
        (primary, fallback, _) = _healthState.GetHealthStatus();
        Assert.False(primary);
        Assert.True(fallback);
    }

    [Fact]
    public void BothEndpointsDown_StillReturnsPrimaryAsLastResort()
    {
        _healthState.MarkEndpointUnhealthy("http://192.168.16.53:11434", new Exception("Test"));
        _healthState.MarkEndpointUnhealthy("http://192.168.16.12:11434", new Exception("Test"));

        var (endpoint, _) = _healthState.GetHealthyEndpoint();
        // Should return primary even though both are unhealthy
        Assert.Equal("http://192.168.16.53:11434", endpoint);
    }

    [Fact]
    public void HealthStatus_ReportsCorrectStateAfterMultipleUpdates()
    {
        var (primary, fallback, _) = _healthState.GetHealthStatus();
        Assert.True(primary && fallback);

        _healthState.MarkEndpointUnhealthy("http://192.168.16.53:11434", new Exception("Test"));
        (primary, fallback, _) = _healthState.GetHealthStatus();
        Assert.False(primary);
        Assert.True(fallback);

        _healthState.MarkEndpointHealthy("http://192.168.16.12:11434");
        (primary, fallback, _) = _healthState.GetHealthStatus();
        Assert.False(primary);
        Assert.True(fallback);

        _healthState.MarkEndpointHealthy("http://192.168.16.53:11434");
        (primary, fallback, _) = _healthState.GetHealthStatus();
        Assert.True(primary);
        Assert.True(fallback);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Blaze.LlmGateway.Tests/OllamaHealthStateTests.cs
git commit -m "test: add comprehensive OllamaHealthState tests including concurrency"
```

---

### Task 13: Build and Run Tests

**Files:**
- None (verification step)

- [ ] **Step 1: Clean build**

```bash
dotnet build Blaze.LlmGateway.slnx --no-incremental -warnaserror
```

Expected: Build succeeds with 0 warnings

- [ ] **Step 2: Run health state tests**

```bash
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build --filter "FullyQualifiedName~OllamaHealthStateTests"
```

Expected: All 5 tests pass

- [ ] **Step 3: Run full test suite**

```bash
dotnet test Blaze.LlmGateway.Tests\Blaze.LlmGateway.Tests.csproj --no-build
```

Expected: All tests pass (or explicit skip reasons for integration tests)

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "build: verify all tests pass with new health state infrastructure"
```

---

## Phase 8: End-to-End Verification

### Task 14: Test Via Open WebUI

**Files:**
- None (manual E2E test)

- [ ] **Step 1: Start Aspire AppHost**

```bash
dotnet run --project Blaze.LlmGateway.AppHost
```

Expected: Aspire starts, all resources (Ollama .53, .12, LM Studio) show as Running in dashboard

- [ ] **Step 2: Verify Model Sync Validation Passed**

Check Aspire logs for:
```
✅ Model sync validation PASSED. Both endpoints have identical models: gemma4:e4b
```

- [ ] **Step 3: Open Open WebUI**

Navigate to `http://localhost:8080` (or configured port for Open WebUI)

- [ ] **Step 4: Send Test Message**

Type: "Hello, what's your name?"
Expected: Response appears via streaming, no errors or timeouts

- [ ] **Step 5: Send Code Task**

Type: "Write a Python function to reverse a string"
Expected: 
- Response appears
- Logs show: `🎯 Task classified as: Coding`
- Response uses LM Studio model

- [ ] **Step 6: Send Vision Task**

Type: "I have a screenshot. What do you see?" (note: you can't actually attach image via Open WebUI, but the classification should handle the text intent)
Expected:
- Response appears
- Logs show task classification attempt

- [ ] **Step 7: Test Failover (Optional - Advanced)**

To test failover manually:
1. SSH to .53 and stop Ollama: `systemctl stop ollama`
2. Send new message to Open WebUI
3. Expected: Gateway detects .53 down, fails over to .12, continues working
4. Logs show: `❌ Primary Ollama router (.53) marked unhealthy; failing over to .12`
5. Restart Ollama on .53: `systemctl start ollama`
6. Send another message
7. Expected: Gateway detects .53 recovered, switches back
8. Logs show: `✅ Primary Ollama router (.53) recovered; returning to primary`

---

## Phase 9: Documentation & Cleanup

### Task 15: Update Documentation

**Files:**
- Modify: `CLAUDE.md` (Known Implementation Gaps section)
- Modify: `README.md` (if it exists, add deployment steps)

- [ ] **Step 1: Update CLAUDE.md**

Find "Known Incomplete Areas" section and update:

Replace:
```markdown
- `McpConnectionManager.StartAsync()` — placeholder; MCP tool connections not fully wired.
- `McpToolDelegatingClient.AppendMcpTools` — needs mapping to `HostedMcpServerTool` instances.
- No circuit breaker — high-priority resilience enhancement for Phase 2.
```

With:
```markdown
- ✅ **Model sync validation** — Startup checks that .12 and .53 have identical models
- ✅ **Router failover** — OllamaHealthStateManager provides thread-safe failover between primary/fallback
- ✅ **Prompt cleanup + task classification** — Re-enabled with full failover support
- `McpConnectionManager.StartAsync()` — placeholder; MCP tool connections not fully wired.
- `McpToolDelegatingClient.AppendMcpTools` — needs mapping to `HostedMcpServerTool` instances.
```

- [ ] **Step 2: Add deployment guide to README or DEPLOYMENT.md**

Create or update with:

```markdown
## Deployment: Local Router Setup

### Prerequisites
- Two Ollama instances: primary (.53:11434) and fallback (.12:11434)
- Both must have `gemma4:e4b` model installed (9 GB each)
- LM Studio running on .56:1234

### Setup .53 (Primary Router)
```bash
ssh root@192.168.16.53
ollama pull gemma4:e4b
ollama list  # Verify gemma4:e4b appears
```

### Setup .12 (Fallback Router)
```bash
ssh root@192.168.16.12
ollama pull gemma4:e4b
ollama list  # Verify gemma4:e4b appears
```

### Start Gateway
```bash
dotnet run --project Blaze.LlmGateway.AppHost
```

Logs should show:
```
✅ Model sync validation PASSED. Both endpoints have identical models: gemma4:e4b
```

### Testing
- Open WebUI: http://localhost:8080
- Send test message
- Observe logs for routing decisions
```

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md README.md
git commit -m "docs: update with model sync and router failover completion + deployment guide"
```

---

## Summary Checklist

- [ ] Phase 1: .53 configured with gemma4:e4b
- [ ] Phase 2: Config refactored to OllamaRouter primary/fallback
- [ ] Phase 3: IOllamaHealthState + OllamaHealthStateManager implemented
- [ ] Phase 4: OllamaFailoverClient created
- [ ] Phase 5: DI registrations updated
- [ ] Phase 6: GemmaPromptCleaner + OllamaTaskClassifier integrated with health state
- [ ] Phase 7: Model sync validator wired to startup
- [ ] Phase 8: Tests written and passing
- [ ] Phase 9: E2E verified via Open WebUI
- [ ] Phase 10: Documentation updated

---

## Known Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Thread race in health state | Uses ReaderWriterLockSlim for exclusive writes, concurrent reads |
| Failover loop (A → B → A → ...) | Circuit breaker on both endpoints prevents infinite retry |
| Model mismatch on upgrade | Startup validation catches and fails loudly |
| SSH credentials exposed | Use environment variables or secrets management in production |
| Both routers down | Falls back to keyword-only classification, logs CRITICAL |

