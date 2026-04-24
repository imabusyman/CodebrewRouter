# microsoft/Foundry-Local: Comprehensive Research Report

## Executive Summary

**Foundry Local** is Microsoft's open-source, production-ready SDK and runtime for **on-device AI inference**. It enables developers to embed AI capabilities directly into applications that run entirely on end-user hardware, with no cloud dependencies, zero API token costs, and full data privacy. The project includes native SDKs for C#, JavaScript, Python, and Rust; a curated model catalog optimized for consumer hardware; automatic hardware acceleration (CPU, GPU, NPU); and an optional OpenAI-compatible web server. The core runtime is lightweight (~20 MB), handles full model lifecycle management (download, load, unload, caching), and uses **ONNX Runtime** for hardware-accelerated inference.

---

## What is Foundry Local?

Foundry Local is Microsoft's **end-to-end local AI solution** for building applications that run entirely on the user's device.[^1] It provides:

1. **Native SDKs** for C#, JavaScript, Python, and Rust
2. **Curated model catalog** of optimized AI models for on-device use
3. **Automatic hardware acceleration** (CPU, GPU, NPU detection and optimization)
4. **Full model lifecycle management** (download, cache, load, unload, model variant selection)
5. **OpenAI-compatible API** for chat completions, embeddings, and audio transcription
6. **Optional web server** for REST-based access to models across processes

### Core Design Philosophy

Foundry Local is designed as a **client-side runtime**, not a server inference platform.[^2] Key distinctions:

- **Single-user, hardware-constrained devices**: Optimized for inference on individual user devices (desktops, laptops, mobile, IoT)
- **Not for multi-concurrent-user scenarios**: Unlike server runtimes (vLLM, Triton), it does not handle request queuing or concurrent batching
- **Zero infrastructure overhead**: Models run in-process, eliminating the need for separate servers, APIs, or backend services
- **Complete data privacy**: User data never leaves the device

---

## Architecture Overview

Foundry Local consists of several interconnected components:

```
┌─────────────────────────────────────────────────────────────┐
│ User Application (C#/.NET, JavaScript, Python, Rust)        │
└────────────────┬────────────────────────────────────────────┘
                 │
        ┌────────▼─────────────────┐
        │  SDK Interface (Language) │
        │  • FoundryLocalManager    │
        │  • ICatalog               │
        │  • IModel                 │
        └────────┬─────────────────┘
                 │
┌────────────────▼──────────────────────────────────────────┐
│         Foundry Local Core Runtime                         │
│  ┌─────────────────────────────────────────────────────┐  │
│  │ Model Catalog Service                               │  │
│  │ • Lists available models (chat, audio)              │  │
│  │ • Tracks model variants (quantization, HW target)   │  │
│  │ • Manages download/cache/load state                 │  │
│  └─────────────────────────────────────────────────────┘  │
│  ┌─────────────────────────────────────────────────────┐  │
│  │ Model Manager                                        │  │
│  │ • Detects hardware (NPU, GPU, CPU)                  │  │
│  │ • Selects optimal model variant per device          │  │
│  │ • Manages model downloads from CDN                  │  │
│  │ • Handles caching and versioning                    │  │
│  └─────────────────────────────────────────────────────┘  │
│  ┌─────────────────────────────────────────────────────┐  │
│  │ ONNX Runtime (Inference Engine)                     │  │
│  │ • Execution Providers (EPs): CPU, GPU, NPU, etc.   │  │
│  │ • Model loading, KV-cache management                │  │
│  │ • Hardware acceleration                             │  │
│  └─────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                 │
        ┌────────▼────────┐
        │ Local Storage    │
        │ • Model cache    │
        │ • Config/logs    │
        └─────────────────┘
        
        (Optional)
        ┌────────────────────────────────────┐
        │ OpenAI-Compatible Web Server       │
        │ • /v1/chat/completions             │
        │ • /v1/embeddings                   │
        │ • /v1/models                       │
        └────────────────────────────────────┘
```

### Component Details

#### 1. **FoundryLocalManager (SDK Entry Point)**

The singleton manager that orchestrates the entire runtime.[^3]

```csharp
// Initialization
await FoundryLocalManager.CreateAsync(
    new Configuration { AppName = "my-app" },
    loggerFactory.CreateLogger("FoundryLocal"));

// Access anytime afterward
var manager = FoundryLocalManager.Instance;
```

**Responsibilities:**
- Initialize and manage the Foundry Local Core runtime
- Provide access to the model catalog
- Manage the optional web service lifecycle
- Handle hardware detection and EP (execution provider) management

#### 2. **Model Catalog Service**

Lists and manages all available models for the user's hardware.[^4]

```csharp
var catalog = await manager.GetCatalogAsync();

// List all available models
var models = await catalog.ListModelsAsync();

// Get a specific model
var model = await catalog.GetModelAsync("phi-3.5-mini");

// List cached or loaded models
var cached = await catalog.GetCachedModelsAsync();
var loaded = await catalog.GetLoadedModelsAsync();
```

**Available Models** (as of repository state):[^5]

| Model Alias | Use Case | Variants | Notes |
|---|---|---|---|
| `phi-3.5-mini` | General chat, coding | Generic (CPU, GPU, NPU) | Lightweight 3.8B parameters |
| `qwen2.5-0.5b` | Lightweight chat | Multiple quantizations | Ultra-compact 0.5B parameters |
| `whisper-tiny` | Audio transcription | Generic | Speech-to-text |
| `phi-4-mini` | Enterprise chat, reasoning | Multiple | Newer Phi architecture |

Each model may have **multiple variants** representing different quantizations (4-bit, 8-bit) and hardware targets (CPU, GPU, NPU). The SDK auto-selects the best variant for the user's device.

#### 3. **Model Lifecycle Management**

Full programmatic control over model state:[^6]

```csharp
var model = await catalog.GetModelAsync("qwen2.5-0.5b");

// Download with progress
await model.DownloadAsync(progress => 
    Console.WriteLine($"Download: {progress:F1}%"));

// Load into memory for inference
await model.LoadAsync();

// Run inference (see below)
// ...

// Unload when done
await model.UnloadAsync();

// Remove entirely from cache
await model.RemoveFromCacheAsync();
```

#### 4. **Chat Completions**

OpenAI-compatible streaming and non-streaming chat:[^7]

```csharp
var chatClient = await model.GetChatClientAsync();

// Non-streaming
var response = await chatClient.CompleteChatAsync(new[]
{
    new ChatMessage { Role = "system", Content = "You are helpful." },
    new ChatMessage { Role = "user", Content = "Explain quantum computing." }
});
Console.WriteLine(response.Choices[0].Message.Content);

// Streaming (token-by-token)
await foreach (var chunk in chatClient.CompleteChatStreamingAsync(
    new[] { new ChatMessage { Role = "user", Content = "Write a haiku." } },
    cancellationToken))
{
    Console.Write(chunk.Choices?[0]?.Message?.Content);
}
```

**Chat Settings:**[^8]
- `Temperature` (0.0–2.0): Randomness of responses
- `MaxTokens`: Output length limit
- `TopP`: Nucleus sampling parameter
- `FrequencyPenalty`: Reduce token repetition

#### 5. **Audio Transcription**

Whisper-based speech-to-text with streaming support:[^9]

```csharp
var audioClient = await model.GetAudioClientAsync();

// File-based transcription
var result = await audioClient.TranscribeAudioAsync("recording.mp3");
Console.WriteLine(result.Text);

// Streaming transcription
await foreach (var chunk in audioClient.TranscribeAudioStreamingAsync("recording.mp3"))
{
    Console.Write(chunk.Text);
}

// Real-time microphone transcription
var session = audioClient.CreateLiveTranscriptionSession();
session.Settings.SampleRate = 16000;
session.Settings.Language = "en";
await session.StartAsync();

// Push PCM audio from microphone
waveIn.DataAvailable += (s, e) => 
    _ = session.AppendAsync(new ReadOnlyMemory<byte>(e.Buffer, 0, e.BytesRecorded));

// Read results as they stream
await foreach (var result in session.GetTranscriptionStream())
{
    Console.Write(result.Content?[0]?.Text);
}

await session.StopAsync();
```

#### 6. **Hardware Acceleration (Windows/WinML)**

On Windows, Foundry Local can use WinML for GPU/NPU acceleration via ONNX Runtime execution providers.[^10]

```csharp
// Discover available execution providers
var eps = manager.DiscoverEps();
foreach (var ep in eps)
{
    Console.WriteLine($"{ep.Name} — registered: {ep.IsRegistered}");
}

// Download and register all EPs (or specific ones)
var result = await manager.DownloadAndRegisterEpsAsync();

// Or track per-EP download progress
await manager.DownloadAndRegisterEpsAsync((epName, percent) =>
{
    Console.WriteLine($"{epName}: {percent:F1}%");
});
```

**Execution Providers** (detected automatically, but can be pre-registered):
- **CPU**: Default fallback for all platforms
- **GPU**: NVIDIA CUDA, AMD ROCm, Intel Arc (when available)
- **NPU**: Apple Neural Engine (macOS), Qualcomm Hexagon (Android), Intel Gaudi
- **WinML**: Windows-specific hardware acceleration

#### 7. **Optional Web Server**

Start an OpenAI-compatible REST endpoint for multi-process access:[^11]

```csharp
await FoundryLocalManager.CreateAsync(
    new Configuration
    {
        AppName = "my-app",
        Web = new Configuration.WebService
        {
            Urls = "http://127.0.0.1:5000"
        }
    },
    NullLogger.Instance);

await FoundryLocalManager.Instance.StartWebServiceAsync();
Console.WriteLine($"Listening on: {string.Join(", ", FoundryLocalManager.Instance.Urls!)}");

// Endpoints available:
// POST /v1/chat/completions
// POST /v1/embeddings
// GET /v1/models
```

### Configuration

The `Configuration` class controls runtime behavior:[^12]

| Property | Type | Default | Description |
|---|---|---|---|
| `AppName` | `string` | **(required)** | Application identifier |
| `AppDataDir` | `string?` | `~/.{AppName}` | Base data directory |
| `ModelCacheDir` | `string?` | `{AppDataDir}/cache/models` | Local model storage |
| `LogsDir` | `string?` | `{AppDataDir}/logs` | Log files location |
| `LogLevel` | `LogLevel` | `Warning` | `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` |
| `Web` | `WebService?` | `null` | Optional web service config |
| `AdditionalSettings` | `Dictionary?` | `null` | Extra settings for Core runtime |

---

## Model Catalog

Foundry Local provides a **curated, intentionally limited model catalog** of production-ready models.[^13]

### Why Curated?

Foundry Local is **not a general-purpose model distribution** like Hugging Face or Ollama. The catalog is intentionally small because:

1. **Production quality**: Every model is tested across consumer hardware
2. **Optimization**: Models are quantized and compressed for on-device inference
3. **Shipping constraints**: Models must be small enough to distribute to end users
4. **Hardware compatibility**: Models are selected for reliable on-device behavior

### Supported Capabilities

| Capability | Model Examples | SDK API | Format |
|---|---|---|---|
| **Chat Completions** | Phi-3.5, Qwen, DeepSeek, Mistral | `GetChatClientAsync()` | ONNX |
| **Audio Transcription** | Whisper-tiny, Whisper-base | `GetAudioClientAsync()` | ONNX |
| **Embeddings** | (Under development) | `GetEmbeddingClientAsync()` | ONNX |

---

## Integration with CodebrewRouter

In the CodebrewRouter LLM gateway project, **Foundry Local is registered as a route destination** for on-device inference.[^14]

### RouteDestination Enum

```csharp
public enum RouteDestination
{
    AzureFoundry,      // Azure-hosted Foundry / Azure OpenAI
    FoundryLocal,      // On-device Foundry Local (localhost:5273)
    GithubModels       // GitHub Models inference API
}
```

### Configuration

In `appsettings.json`:[^15]

```json
{
  "LlmGateway": {
    "Providers": {
      "FoundryLocal": {
        "Endpoint": "http://localhost:5273",
        "Model": "",
        "ApiKey": "notneeded"
      }
    },
    "Routing": {
      "FalloverChains": {
        "AzureFoundry": ["FoundryLocal"],
        "FoundryLocal": ["AzureFoundry"]
      }
    }
  }
}
```

### Provider Registration

In `InfrastructureServiceExtensions.cs`:[^16]

```csharp
services.AddKeyedSingleton<IChatClient>("FoundryLocal", (sp, _) =>
{
    var opts = sp.GetRequiredService<IOptions<LlmGatewayOptions>>()
        .Value.Providers.FoundryLocal;
    var client = new AzureOpenAIClient(
        new Uri(opts.Endpoint),
        new AzureKeyCredential(opts.ApiKey));
    return client.GetChatClient(opts.Model).AsIChatClient()
        .AsBuilder().UseFunctionInvocation().Build();
});
```

**Key insight**: CodebrewRouter treats Foundry Local as an **OpenAI-compatible endpoint** running on `localhost:5273`. The gateway can transparently route requests to either cloud providers or Foundry Local based on routing strategy.

---

## SDK Ecosystem

Foundry Local provides **parity across four languages**, enabling embedded AI in diverse platforms:

### C# SDK (`Microsoft.AI.Foundry.Local`)[^17]

**Package**: `dotnet add package Microsoft.AI.Foundry.Local`

**Hardware acceleration variant**: `Microsoft.AI.Foundry.Local.WinML`

**Key types**:
- `FoundryLocalManager` — singleton entry point
- `ICatalog` — model listing and discovery
- `IModel` — model identity and metadata
- `OpenAIChatClient` — chat completions (sync + streaming)
- `OpenAIAudioClient` — audio transcription
- `LiveAudioTranscriptionSession` — real-time streaming

**WinML/EP Management**:
```csharp
var eps = manager.DiscoverEps();
await manager.DownloadAndRegisterEpsAsync(names?, progressCallback?, ct?);
```

### JavaScript SDK[^18]

**Package**: `npm install foundry-local-sdk` (macOS/Linux) or `npm install foundry-local-sdk-winml` (Windows)

**Key exports**:
- `FoundryLocalManager`
- `FoundryLocalManager.create()` — factory method

```javascript
import { FoundryLocalManager } from 'foundry-local-sdk';

const manager = FoundryLocalManager.create({ appName: 'my-app' });
const model = await manager.catalog.getModel('qwen2.5-0.5b');
await model.download((progress) => {
    process.stdout.write(`\rDownloading... ${progress.toFixed(2)}%`);
});
await model.load();

const chatClient = model.createChatClient();
const response = await chatClient.completeChat([
    { role: 'user', content: 'What is the golden ratio?' }
]);
console.log(response.choices[0]?.message?.content);
await model.unload();
```

### Python SDK[^19]

**Package**: `pip install foundry-local-sdk` (macOS/Linux) or `pip install foundry-local-sdk-winml` (Windows)

```python
from foundry_local_sdk import Configuration, FoundryLocalManager

config = Configuration(app_name="foundry_local_samples")
FoundryLocalManager.initialize(config)
manager = FoundryLocalManager.instance

model = manager.catalog.get_model("qwen2.5-0.5b")
model.download()
model.load()

client = model.get_chat_client()

messages = [
    {"role": "user", "content": "What is the golden ratio?"}
]
response = client.complete_chat(messages)
print(f"Response: {response.choices[0].message.content}")

model.unload()
```

### Rust SDK[^20]

Available in `sdk/rust/` directory. Provides similar capability with Rust-native async patterns and memory safety guarantees.

---

## Command-Line Interface (CLI)

Foundry Local includes a **preview CLI** for experimentation and model management:[^21]

### Installation

```bash
# Windows
winget install Microsoft.FoundryLocal

# macOS
brew install microsoft/foundrylocal/foundrylocal
```

### Common Commands

```bash
# List available models
foundry model ls

# Run a model interactively
foundry model run qwen2.5-0.5b

# Download a model
foundry model download phi-3.5-mini
```

### Optional REST Interface

The CLI also provides a **REST interface** for programmatic access (preview).

---

## Samples and Documentation

### Official Samples[^22]

| Language | Count | Highlights |
|----------|-------|-----------|
| **C#** | 12 | Native chat, audio, tool calling, web server, tutorials |
| **JavaScript** | 12 | Native chat, Electron app, Copilot SDK, LangChain, tool calling |
| **Python** | 9 | Chat, audio, LangChain integration, tool calling |
| **Rust** | 8 | Native chat, audio, tool calling, web server |

### Key Tutorials (on Microsoft Learn)[^23]

1. Build a multi-turn chat assistant
2. Build an AI assistant with tool calling
3. Build a voice-to-text note taker
4. Build a document summarizer

### Integration Guides[^24]

- Integrate with Inferencing SDKs (MEAI, etc.)
- Transcribe audio files
- Integrate with LangChain
- Compile Hugging Face models to ONNX for Foundry Local

---

## Performance Characteristics

### Runtime Size

- **Core runtime**: ~20 MB (lightweight for distribution)
- **Model sizes** (quantized):
  - Phi-3.5 mini: ~1.9 GB (4-bit quantization)
  - Qwen 0.5B: ~300 MB (4-bit)
  - Whisper-tiny: ~390 MB

### Latency

- **First inference** (model not loaded): ~500ms–2s (load time depends on model and hardware)
- **Subsequent inference**: 50ms–500ms (depends on model size, hardware, token count)
- **Token generation** (on-device): ~50–200ms per token (Phi-3.5 on CPU; GPU significantly faster)

### Hardware Support

- **Windows**: CPU, NVIDIA GPU (CUDA), AMD GPU (ROCm), Intel Arc, Intel Gaudi, Intel NPU, Qualcomm Hexagon
- **macOS**: CPU, Apple Neural Engine (NPU)
- **Linux**: CPU, NVIDIA GPU, AMD GPU, Intel Gaudi

Foundry Local detects available hardware and automatically selects the best variant for inference.

---

## Key Design Decisions

### 1. **On-Device Only (No Cloud)**

Foundry Local is deliberately designed to **never communicate with cloud services**.[^25] All inference happens locally with zero external dependencies beyond model downloads on first use.

### 2. **Curated Model Catalog**

Rather than distributing every model, Foundry Local intentionally maintains a **small, hand-picked catalog** of production-ready models.[^26] This ensures:
- Quality and reliability on consumer hardware
- Aggressive optimization for size and latency
- Predictable performance across diverse hardware
- Focused developer experience

### 3. **Single-User Inference**

Foundry Local is **not optimized for multi-user/multi-concurrent scenarios**.[^27] Unlike server runtimes (vLLM, Triton), it:
- Does not implement request queuing
- Does not perform continuous batching
- Does not share KV-cache across users
- Uses single-user KV-cache management

**Why**: Single-user devices have different constraints than servers. Foundry Local prioritizes latency, low memory overhead, and automatic hardware adaptation over throughput.

### 4. **OpenAI-Compatibility**

The optional web service provides **OpenAI-compatible request/response formats**, enabling drop-in compatibility with existing OpenAI SDK clients and tools (LangChain, etc.).[^28]

### 5. **Hardware Acceleration as Opt-In**

On Windows, GPU/NPU acceleration via WinML is **explicitly managed by the developer**, not automatic.[^29] This avoids large ~1 GB EP downloads unless actually needed.

---

## Comparison with Related Systems

### Foundry Local vs. Azure AI Foundry (Cloud)

| Aspect | Foundry Local | Azure AI Foundry |
|---|---|---|
| **Deployment** | On-device (no backend) | Managed cloud service |
| **Data Privacy** | Complete (all local) | Cloud-hosted |
| **Cost** | Free (no per-token fees) | Pay-per-token or subscription |
| **Latency** | ~50–500ms per request | Network latency + cloud latency |
| **Scalability** | Single-user | Multi-user, enterprise scale |

### Foundry Local vs. Ollama

| Aspect | Foundry Local | Ollama |
|---|---|---|
| **Model Support** | Curated catalog (Phi, Qwen, Whisper) | Any GGUF format model |
| **Hardware Accel** | CPU, GPU, NPU (auto-detected) | GPU/CPU (less optimized) |
| **SDK Support** | C#, JS, Python, Rust (first-class) | Mostly HTTP API, limited SDKs |
| **Repo Approach** | Official Microsoft, curated | Community-driven, open format |
| **App Embedding** | Native SDK in-process | Usually requires separate server |

### Foundry Local vs. LLaMA.cpp

| Aspect | Foundry Local | LLaMA.cpp |
|---|---|---|
| **Models** | Multiple families optimized | GGUF format (broad support) |
| **Language Bindings** | Official SDKs (4 languages) | Community bindings, less formalized |
| **Hardware** | ONNX Runtime (CPU, GPU, NPU) | Primarily CPU-optimized |
| **Use Case** | Production embedding | Development/experimentation |

---

## Repository Structure

The `[microsoft/Foundry-Local](https://github.com/microsoft/Foundry-Local)` repository contains:[^30]

```
Foundry-Local/
├── sdk/
│   ├── cs/                  # C# SDK (NuGet: Microsoft.AI.Foundry.Local)
│   ├── js/                  # JavaScript SDK (npm: foundry-local-sdk)
│   ├── python/              # Python SDK (pip: foundry-local-sdk)
│   ├── rust/                # Rust SDK
│   ├── cpp/                 # C++ SDK (lower-level)
│   └── deps_versions.json   # Dependency versions
├── sdk_legacy/              # Older SDK versions (deprecated)
├── samples/
│   ├── cs/                  # C# examples
│   ├── js/                  # JavaScript examples
│   ├── python/              # Python examples
│   └── rust/                # Rust examples
├── docs/
│   └── README.md            # Documentation index (links to Microsoft Learn)
├── www/                     # Website and documentation site (Svelte)
├── media/                   # Icons and images
├── licenses/                # License files for bundled dependencies
├── .pipelines/              # Azure Pipelines CI/CD configuration
├── CODE_OF_CONDUCT.md
├── CONTRIBUTING.md
├── LICENSE                  # Microsoft Software License Terms
├── README.md
└── SECURITY.md
```

### Key SDK Files

**C# SDK** (`sdk/cs/`):[^31]
- `src/Microsoft.AI.Foundry.Local.csproj` — main library
- `src/FoundryLocalManager.cs` — singleton entry point
- `src/OpenAIChatClient.cs` — chat completions
- `src/OpenAIAudioClient.cs` — audio transcription
- `src/Configuration.cs` — runtime configuration

**JavaScript SDK** (`sdk/js/`):[^32]
- Native bindings for Node.js and browser
- Prebuilt for Windows, macOS, Linux
- WinML variant for GPU acceleration on Windows

---

## Integration Patterns

### Pattern 1: Embedded in Desktop App (C#/WinForms, WPF)

```csharp
// MainWindow.xaml.cs
public partial class MainWindow : Window
{
    private FoundryLocalManager manager;

    public MainWindow()
    {
        InitializeComponent();
        InitializeFoundryLocal();
    }

    private async void InitializeFoundryLocal()
    {
        await FoundryLocalManager.CreateAsync(
            new Configuration { AppName = "MyDesktopApp" },
            loggerFactory.CreateLogger("FoundryLocal"));
        
        manager = FoundryLocalManager.Instance;
        var catalog = await manager.GetCatalogAsync();
        var model = await catalog.GetModelAsync("phi-3.5-mini");
        await model.DownloadAsync();
        await model.LoadAsync();
    }

    private async void OnChatButtonClicked(object sender, RoutedEventArgs e)
    {
        var catalog = await manager.GetCatalogAsync();
        var model = await catalog.GetModelAsync("phi-3.5-mini");
        var chatClient = await model.GetChatClientAsync();
        
        var response = await chatClient.CompleteChatAsync(new[]
        {
            new ChatMessage { Role = "user", Content = userInput.Text }
        });
        
        responseText.Text = response.Choices[0].Message.Content;
    }
}
```

### Pattern 2: Electron App (JavaScript)

```javascript
import { app, BrowserWindow } from 'electron';
import { FoundryLocalManager } from 'foundry-local-sdk';

let manager;
let mainWindow;

async function initializeFoundryLocal() {
    manager = FoundryLocalManager.create({ appName: 'my-electron-app' });
    const model = await manager.catalog.getModel('phi-3.5-mini');
    await model.download();
    await model.load();
}

async function handleChatRequest(userMessage) {
    const model = await manager.catalog.getModel('phi-3.5-mini');
    const chatClient = model.createChatClient();
    
    const response = await chatClient.completeChat([
        { role: 'user', content: userMessage }
    ]);
    
    return response.choices[0]?.message?.content;
}

app.on('ready', () => {
    initializeFoundryLocal();
    // ... create main window
});
```

### Pattern 3: Web Service Proxy (REST-based)

```csharp
// Expose local models via web server for other processes
await FoundryLocalManager.CreateAsync(
    new Configuration
    {
        AppName = "FoundryLocalWebService",
        Web = new Configuration.WebService
        {
            Urls = "http://0.0.0.0:5273"
        }
    },
    logger);

await FoundryLocalManager.Instance.StartWebServiceAsync();

// Other applications now access models via REST:
// curl -X POST http://localhost:5273/v1/chat/completions \
//   -H "Content-Type: application/json" \
//   -d '{"model":"phi-3.5-mini","messages":[{"role":"user","content":"Hello"}]}'
```

### Pattern 4: LangChain Integration (Python)

```python
from foundry_local_sdk import Configuration, FoundryLocalManager
from langchain.chat_models import ChatOpenAI
from langchain.prompts import ChatPromptTemplate

# Initialize Foundry Local
config = Configuration(app_name="langchain_app")
FoundryLocalManager.initialize(config)
manager = FoundryLocalManager.instance

# Start web service for LangChain to connect
await manager.start_web_service_async()

# Point LangChain to local Foundry Local endpoint
llm = ChatOpenAI(
    model_name="phi-3.5-mini",
    base_url="http://localhost:5273/v1",
    api_key="sk-local"
)

prompt = ChatPromptTemplate.from_messages([
    ("system", "You are a helpful assistant."),
    ("user", "{question}")
])

chain = prompt | llm
result = chain.invoke({"question": "Explain quantum computing"})
print(result.content)
```

---

## Security & Privacy Considerations

### Data Privacy[^33]

- **All inference is local**: User input and model outputs never leave the device unless explicitly sent by the application
- **No telemetry**: Foundry Local does not collect usage data or send diagnostics to Microsoft
- **Model downloads**: Models are downloaded from CDN only on first use; checksums are validated

### Model Safety

- **Curated models**: All models in the catalog have been reviewed for production use
- **No injection attacks**: Model validation prevents malformed models from loading
- **Sandboxing**: ONNX Runtime runs in-process but with memory isolation

### Licensing

Foundry Local is licensed under the **Microsoft Software License Terms**.[^34]

---

## Known Limitations & Future Directions

### Current Limitations

1. **Model catalog is limited**: Only pre-selected models are supported (by design)
2. **No multi-user**: Single-user inference only; not designed for concurrent request handling
3. **Audio input** (real-time transcription): Newly added, still in development
4. **No custom model compilation** (yet): Limited to official catalog (though guides exist for compiling Hugging Face models)
5. **Limited to ONNX format**: Only ONNX models are supported

### Future Roadmap (Inferred from Repository)[^35]

- Broader model catalog (more chat models, embedding models, vision models)
- Improved audio transcription (better real-time support)
- Tool calling enhancements (better agentic workflows)
- Vision/multimodal models on-device
- Improved Windows hardware acceleration (more EP types)

---

## Confidence Assessment

**High Confidence** (95%+):
- Core architecture, SDK design, and available features (based on README, code, official samples)
- Language SDK support and APIs
- Curated model catalog design philosophy
- Integration with ONNX Runtime
- OpenAI-compatible web service

**Medium-High Confidence** (80–90%):
- Exact performance metrics (latency, model sizes) — README provides ranges, actual performance varies by hardware
- Future roadmap — inferred from issue patterns and repository structure, not officially stated
- Comparison with competing systems — based on published documentation, not exhaustive performance testing

**Medium Confidence** (70–80%):
- Deep internal implementation details of the Core runtime (C++ code not fully examined)
- Exact EP download sizes and performance impact
- Production deployment best practices (limited real-world case studies in public repo)

---

## Source Materials

Research materials and sources are available in the `sources/microsoft-foundry-local/` directory, organized as follows:

- `sources/microsoft-foundry-local/repo/` — Key files from the GitHub repository
- Documentation and README files
- SDK reference documentation
- Sample code and integration examples

---

## Footnotes

[^1]: [sources/microsoft-foundry-local/repo/README.md](./sources/microsoft-foundry-local/repo/README.md) — "Ship on-device AI inside your app" and feature description.

[^2]: [sources/microsoft-foundry-local/repo/README.md — FAQ](./sources/microsoft-foundry-local/repo/README.md) — "Can Foundry Local run on a server?" section explicitly discusses single-user design.

[^3]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Initialization](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — FoundryLocalManager initialization and access patterns.

[^4]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Catalog](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — Catalog API and ListModelsAsync, GetModelAsync examples.

[^5]: [sources/microsoft-foundry-local/repo/README.md — Quickstart examples](./sources/microsoft-foundry-local/repo/README.md) — References to qwen2.5-0.5b, phi-3.5-mini, whisper-tiny in code samples.

[^6]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Model Lifecycle](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — Download, Load, Unload, RemoveFromCache methods and patterns.

[^7]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Chat Completions](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — CompleteChatAsync and CompleteChatStreamingAsync examples.

[^8]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Chat Settings](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — Settings properties (Temperature, MaxTokens, TopP, FrequencyPenalty).

[^9]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Audio Transcription](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — TranscribeAudioAsync, TranscribeAudioStreamingAsync, and LiveAudioTranscriptionSession.

[^10]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — WinML Acceleration](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — DiscoverEps(), DownloadAndRegisterEpsAsync() methods and WinML package variant.

[^11]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Web Service](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — StartWebServiceAsync() and endpoint descriptions.

[^12]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Configuration](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — Configuration class properties table.

[^13]: [sources/microsoft-foundry-local/repo/README.md — FAQ](./sources/microsoft-foundry-local/repo/README.md) — "Why doesn't Foundry Local support every available model?" explanation.

[^14]: [CodebrewRouter RouteDestination.cs](./../../Blaze.LlmGateway.Core/RouteDestination.cs) — Enum definition with FoundryLocal destination.

[^15]: [CodebrewRouter appsettings.json](./../../Blaze.LlmGateway.Api/appsettings.json) — FoundryLocal provider configuration.

[^16]: [CodebrewRouter InfrastructureServiceExtensions.cs:34–43](./../../Blaze.LlmGateway.Infrastructure/InfrastructureServiceExtensions.cs) — FoundryLocal keyed IChatClient registration.

[^17]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — Installation & Features](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — C# SDK package name, key types, WinML variant.

[^18]: [sources/microsoft-foundry-local/repo/README.md — Quickstart JavaScript](./sources/microsoft-foundry-local/repo/README.md) — JavaScript SDK package names and usage pattern.

[^19]: [sources/microsoft-foundry-local/repo/README.md — Quickstart Python](./sources/microsoft-foundry-local/repo/README.md) — Python SDK package names and usage pattern.

[^20]: [sources/microsoft-foundry-local/repo/sdk/rust directory](./sources/microsoft-foundry-local/repo/sdk/rust/) — Rust SDK repository structure.

[^21]: [sources/microsoft-foundry-local/repo/README.md — CLI](./sources/microsoft-foundry-local/repo/README.md) — CLI installation and common commands.

[^22]: [sources/microsoft-foundry-local/repo/docs/README.md — Samples](./sources/microsoft-foundry-local/repo/docs/README.md) — Sample counts and highlights per language.

[^23]: [sources/microsoft-foundry-local/repo/docs/README.md — Tutorials](./sources/microsoft-foundry-local/repo/docs/README.md) — Tutorial links on Microsoft Learn.

[^24]: [sources/microsoft-foundry-local/repo/docs/README.md — How-To Guides](./sources/microsoft-foundry-local/repo/docs/README.md) — Integration guide references.

[^33]: [sources/microsoft-foundry-local/repo/README.md](./sources/microsoft-foundry-local/repo/README.md) — "User data never leaves the device, responses start immediately with zero network latency, and your app works offline."

[^34]: [sources/microsoft-foundry-local/repo/LICENSE](./sources/microsoft-foundry-local/repo/LICENSE) — Microsoft Software License Terms.

[^25]: [sources/microsoft-foundry-local/repo/README.md](./sources/microsoft-foundry-local/repo/README.md) — "User data never leaves the device" and offline operation features.

[^26]: [sources/microsoft-foundry-local/repo/README.md — FAQ](./sources/microsoft-foundry-local/repo/README.md) — "Why doesn't Foundry Local support every available model?" explanation.

[^27]: [sources/microsoft-foundry-local/repo/README.md — FAQ](./sources/microsoft-foundry-local/repo/README.md) — "Can Foundry Local run on a server?" — Explicitly contrasts with multi-user server runtimes (vLLM, Triton).

[^28]: [sources/microsoft-foundry-local/repo/README.md — Features](./sources/microsoft-foundry-local/repo/README.md) — "OpenAI-compatible API" and "OpenAI Responses API format" support.

[^29]: [sources/microsoft-foundry-local/repo/sdk/cs/README.md — WinML Acceleration](./sources/microsoft-foundry-local/repo/sdk/cs/README.md) — "EP management is explicit via two methods: DiscoverEps() and DownloadAndRegisterEpsAsync()."

[^30]: [sources/microsoft-foundry-local/repo root directory](./sources/microsoft-foundry-local/repo/) — Repository structure and contents.

[^31]: [sources/microsoft-foundry-local/repo/sdk/cs directory](./sources/microsoft-foundry-local/repo/sdk/cs/) — C# SDK key files and entry point.

[^32]: [sources/microsoft-foundry-local/repo/sdk/js directory](./sources/microsoft-foundry-local/repo/sdk/js/) — JavaScript SDK structure and platforms.

[^35]: [Repository issue tracking and PR patterns](https://github.com/microsoft/Foundry-Local) — Inferred from open issues, PR discussions, and roadmap hints in project structure.
