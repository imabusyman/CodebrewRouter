using System.Text.Json;
using Blaze.LlmGateway.Core.ModelCatalog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for GET /v1/models endpoint.
/// Tests model discovery, provider detection, and response structure.
/// </summary>
public class ModelsIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Blaze.LlmGateway.Api.ApiProgram>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Blaze.LlmGateway.Api.ApiProgram>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    RemoveServicesByType(services, typeof(IChatClient));

                    var mockChatClient = new Mock<IChatClient>();
                    mockChatClient
                        .Setup(c => c.GetStreamingResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(GetStreamingResponse());
                    
                    mockChatClient
                        .Setup(c => c.GetResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ChatResponse(
                            [new ChatMessage(ChatRole.Assistant, "response")]));

                    services.AddSingleton(mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("AzureFoundry", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("FoundryLocal", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("GithubModels", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("OllamaLocal", mockChatClient.Object);
                    services.AddSingleton<IModelCatalog>(new FakeModelCatalog());
                });
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory != null)
            await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Models_GetRequest_ReturnsJsonList()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("object", out var objectProp));
        Assert.Equal("list", objectProp.GetString());
        Assert.True(json.RootElement.TryGetProperty("data", out var dataProp));
        Assert.Equal(JsonValueKind.Array, dataProp.ValueKind);
    }

    [Fact]
    public async Task Models_ResponseStructure_HasCorrectFormat()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("object", out _));
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("id", out _), "Model missing 'id'");
            Assert.True(model.TryGetProperty("object", out _), "Model missing 'object'");
            Assert.True(model.TryGetProperty("provider", out _), "Model missing 'provider'");
            Assert.True(model.TryGetProperty("source", out _), "Model missing 'source'");
        }
    }

    [Fact]
    public async Task Models_DataArray_NotEmpty()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var modelCount = data.GetArrayLength();
        Assert.True(modelCount > 0, "Models list should not be empty");
    }

    [Fact]
    public async Task Models_ContainsKnownProviders()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var providers = new HashSet<string>();

        foreach (var model in data.EnumerateArray())
        {
            if (model.TryGetProperty("provider", out var provider))
            {
                providers.Add(provider.GetString() ?? "");
            }
        }

        var knownProviders = new[] { "AzureFoundry", "FoundryLocal", "GithubModels", "OllamaLocal" };
        var hasKnownProvider = providers.Any(p => knownProviders.Contains(p));
        Assert.True(hasKnownProvider, $"No known providers found. Found: {string.Join(", ", providers)}");
    }

    [Fact]
    public async Task Models_EachModelHasObject_EqualToModel()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("object", out var objectProp));
            Assert.Equal("model", objectProp.GetString());
        }
    }

    [Fact]
    public async Task Models_IdField_NotEmpty()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("id", out var idProp));
            var id = idProp.GetString();
            Assert.False(string.IsNullOrWhiteSpace(id), "Model ID should not be empty");
        }
    }

    [Fact]
    public async Task Models_ProviderField_NotEmpty()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("provider", out var providerProp));
            var provider = providerProp.GetString();
            Assert.False(string.IsNullOrWhiteSpace(provider), "Provider should not be empty");
        }
    }

    [Fact]
    public async Task Models_ContentType_IsApplicationJson()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Models_ResponseIsValidJson()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();

        // Should not throw
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.NotNull(json);
    }

    [Fact]
    public async Task Models_MultipleModels_AllHaveConsistentStructure()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        int count = 0;
        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("id", out _));
            Assert.True(model.TryGetProperty("object", out _));
            Assert.True(model.TryGetProperty("provider", out _));
            count++;
        }

        Assert.True(count > 0, "Should have at least one model");
    }

    [Fact]
    public async Task Models_OwnedByField_IsOptional()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var modelCount = data.GetArrayLength();
        Assert.True(modelCount > 0);
    }

    [Fact]
    public async Task Models_ProviderNames_AreValid()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        var validProviders = new[] { "AzureFoundry", "FoundryLocal", "GithubModels", "OllamaLocal", "CodebrewRouter" };

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("provider", out var providerProp));
            var provider = providerProp.GetString();
            Assert.Contains(provider, validProviders);
        }
    }

    [Fact]
    public async Task Models_ConsistentObjectProperty_AcrossAllModels()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("object", out var obj));
            Assert.Equal("model", obj.GetString());
        }
    }

    [Fact]
    public async Task Models_ResponseStatus_IsOk()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    private static void RemoveServicesByType(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponse()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "response");
        await Task.CompletedTask;
    }

    private sealed class FakeModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AvailableModel>>([
                new AvailableModel("gpt-4o", "AzureFoundry", "openai", "configured"),
                new AvailableModel("gemma4:e4b", "OllamaLocal", "ollama", "live")
            ]);

        public Task<AvailableModel?> FindByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult<AvailableModel?>(null);
    }
}
