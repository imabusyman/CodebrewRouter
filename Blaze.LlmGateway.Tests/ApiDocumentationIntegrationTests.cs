using System.Text.Json;
using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for OpenAPI, Swagger UI, and Scalar documentation endpoints.
/// </summary>
public sealed class ApiDocumentationIntegrationTests : IAsyncLifetime
{
    private WebApplicationFactory<Blaze.LlmGateway.Api.ApiProgram>? _factory;
    private HttpClient? _client;

    public Task InitializeAsync()
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
                    services.AddKeyedSingleton<IChatClient>("GithubCopilot", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("Gemini", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("OpenRouter", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("FoundryLocal", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("GithubModels", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("OllamaLocal", mockChatClient.Object);
                    services.AddSingleton<IModelCatalog>(new FakeModelCatalog());
                    services.AddSingleton<IModelSelectionResolver>(new FakeModelSelectionResolver(mockChatClient.Object));
                });
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task OpenApiDocument_IsAvailable()
    {
        var response = await _client!.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SwaggerDocument_IsAvailable()
    {
        var response = await _client!.GetAsync("/openapi/v1.swagger.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SwaggerUi_IsAvailable()
    {
        var response = await _client!.GetAsync("/swagger/index.html");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Swagger UI", body);
    }

    [Fact]
    public async Task ScalarUi_IsAvailable()
    {
        var response = await _client!.GetAsync("/scalar");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Blaze.LlmGateway API Reference", body);
    }

    [Fact]
    public async Task SwaggerDocument_ContainsLiteLlmEndpoints()
    {
        var response = await _client!.GetAsync("/openapi/v1.swagger.json");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var paths = json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/v1/chat/completions", out _));
        Assert.True(paths.TryGetProperty("/v1/completions", out _));
        Assert.True(paths.TryGetProperty("/v1/models", out _));
    }

    [Fact]
    public async Task SwaggerDocument_ContainsEndpointDescriptions()
    {
        var response = await _client!.GetAsync("/openapi/v1.swagger.json");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        var chatOperation = json.RootElement
            .GetProperty("paths")
            .GetProperty("/v1/chat/completions")
            .GetProperty("post");

        Assert.Equal("Create a chat completion", chatOperation.GetProperty("summary").GetString());
        Assert.Contains("OpenAI-compatible chat request", chatOperation.GetProperty("description").GetString());
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
        yield return new ChatResponseUpdate(ChatRole.Assistant, "hello");
        await Task.CompletedTask;
    }

    private sealed class FakeModelSelectionResolver(IChatClient client) : IModelSelectionResolver
    {
        public Task<IChatClient?> ResolveAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult<IChatClient?>(client);
    }

    private sealed class FakeModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<AvailableModel>> GetAvailableModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AvailableModel>>([
                new AvailableModel("gpt-4", "AzureFoundry", "openai", "configured")
            ]);

        public Task<AvailableModel?> FindByIdAsync(string modelId, CancellationToken cancellationToken = default)
            => Task.FromResult<AvailableModel?>(new AvailableModel(modelId, "AzureFoundry", "openai", "configured"));
    }
}
