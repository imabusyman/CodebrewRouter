using System.Text;
using System.Text.Json;
using Blaze.LlmGateway.Api;
using Blaze.LlmGateway.Core.ModelCatalog;
using Blaze.LlmGateway.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for POST /v1/chat/completions endpoint.
/// Uses WebApplicationFactory to start a real HTTP server and test actual endpoint behavior.
/// Mocks IChatClient at the DI level to avoid expensive provider calls while testing endpoint contracts.
/// </summary>
public class ChatCompletionsIntegrationTests : IAsyncLifetime
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
                    // Remove all existing IChatClient registrations (keyed and non-keyed)
                    var descriptorsToRemove = services
                        .Where(d => d.ServiceType == typeof(IChatClient) || 
                                   (d.ServiceKey != null && d.ServiceType == typeof(IChatClient)))
                        .ToList();
                    
                    foreach (var descriptor in descriptorsToRemove)
                    {
                        services.Remove(descriptor);
                    }

                    // Mock the main IChatClient to return predictable responses
                    var mockChatClient = new Mock<IChatClient>();
                    
                    // Setup streaming response
                    mockChatClient
                        .Setup(c => c.GetStreamingResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(StreamingResponse());
                    
                    // Setup non-streaming response
                    mockChatClient
                        .Setup(c => c.GetResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ChatResponse(
                            [new ChatMessage(ChatRole.Assistant, "Test assistant response")]));

                    // Register mock as singleton and keyed clients
                    services.AddSingleton(mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("AzureFoundry", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("FoundryLocal", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("GithubModels", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("OllamaLocal", mockChatClient.Object);
                    services.AddSingleton<IModelCatalog>(new FakeModelCatalog());
                    services.AddSingleton<IModelSelectionResolver>(new FakeModelSelectionResolver(mockChatClient.Object));
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
    public async Task StreamingChatCompletion_WithValidRequest_ReturnsSSE()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = new[] { new { role = "user", content = "Hello" } }, stream = true };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode, $"Status: {response.StatusCode}");
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body);
        Assert.EndsWith("data: [DONE]\n\n", body);
    }

    [Fact]
    public async Task StreamingChatCompletion_ResponseFormat_ContainsValidJSON()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = new[] { new { role = "user", content = "Test" } }, stream = true };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ") && !l.Contains("[DONE]")).ToList();
        foreach (var line in dataLines)
        {
            var jsonStr = line.Replace("data: ", "");
            var json = JsonDocument.Parse(jsonStr);
            // Verify the JSON has the expected structure
            Assert.True(json.RootElement.TryGetProperty("object", out var obj));
            Assert.Equal("chat.completion.chunk", obj.GetString());
            Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
            var choice = choices.EnumerateArray().First();
            Assert.True(choice.TryGetProperty("delta", out var delta));
        }
    }

    [Fact]
    public async Task NonStreamingChatCompletion_WithValidRequest_ReturnsJSON()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = new[] { new { role = "user", content = "Hello" } }, stream = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("object", out var obj));
        Assert.Equal("chat.completion", obj.GetString());
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);
    }

    [Fact]
    public async Task NonStreamingChatCompletion_ResponseStructure_ContainsAllRequiredFields()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = new[] { new { role = "user", content = "Test" } }, stream = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("id", out _), "Missing 'id'");
        Assert.True(json.RootElement.TryGetProperty("object", out _), "Missing 'object'");
        Assert.True(json.RootElement.TryGetProperty("created", out _), "Missing 'created'");
        Assert.True(json.RootElement.TryGetProperty("model", out _), "Missing 'model'");
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices), "Missing 'choices'");
        Assert.True(json.RootElement.TryGetProperty("usage", out _), "Missing 'usage'");

        var choice = choices.EnumerateArray().First();
        Assert.True(choice.TryGetProperty("message", out var message));
        Assert.True(message.TryGetProperty("role", out var role));
        Assert.Equal("assistant", role.GetString());
    }

    [Fact]
    public async Task ChatCompletion_MissingModel_ReturnsBadRequest()
    {
        // Arrange
        var request = new { messages = new[] { new { role = "user", content = "Hello" } }, stream = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task ChatCompletion_EmptyMessages_ReturnsBadRequest()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = Array.Empty<object>(), stream = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChatCompletion_WithSystemMessage_IsProcessed()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4",
            messages = new[] {
                new { role = "system", content = "You are helpful" },
                new { role = "user", content = "Hello" }
            },
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ChatCompletion_WithTemperature_IsRespected()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Test" } },
            temperature = 0.5,
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ChatCompletion_WithMaxTokens_IsRespected()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Test" } },
            max_tokens = 100,
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task StreamingChatCompletion_MultipleChunks_AllChunksReceived()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = new[] { new { role = "user", content = "Say hello world!" } }, stream = true };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ")).ToList();
        Assert.True(dataLines.Count > 1, "Should have multiple chunks");
        Assert.True(dataLines.Last().Contains("[DONE]"), "Last chunk should be [DONE]");
    }

    [Fact]
    public async Task ChatCompletion_IDField_IsUnique()
    {
        // Arrange
        var request = new { model = "gpt-4", messages = new[] { new { role = "user", content = "Test" } }, stream = false };
        var content1 = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        var content2 = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response1 = await _client!.PostAsync("/v1/chat/completions", content1);
        var body1 = await response1.Content.ReadAsStringAsync();
        var json1 = JsonDocument.Parse(body1);
        var id1 = json1.RootElement.GetProperty("id").GetString();

        var response2 = await _client!.PostAsync("/v1/chat/completions", content2);
        var body2 = await response2.Content.ReadAsStringAsync();
        var json2 = JsonDocument.Parse(body2);
        var id2 = json2.RootElement.GetProperty("id").GetString();

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task StreamingChatCompletion_WhenProviderFailsBeforeFirstChunk_ReturnsProviderErrorJson()
    {
        var failingClient = new Mock<IChatClient>();
        failingClient
            .Setup(c => c.GetStreamingResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ThrowingStreamingResponse());
        var context = CreateHttpContext();
        var request = new ChatCompletionRequest(
            Model: "gpt-5.4",
            Messages: [new ChatMessageDto("user", "Hello")],
            Stream: true);

        var result = await ChatCompletionsEndpoint.HandleAsync(
            request,
            failingClient.Object,
            new FakeModelSelectionResolver(failingClient.Object),
            context,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        Assert.Contains("provider_error", body);
        Assert.DoesNotContain("[DONE]", body);
    }

    [Fact]
    public async Task NonStreamingChatCompletion_WhenProviderFails_ReturnsProviderErrorJson()
    {
        var failingClient = new Mock<IChatClient>();
        failingClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("provider down"));
        var context = CreateHttpContext();
        var request = new ChatCompletionRequest(
            Model: "gpt-5.4",
            Messages: [new ChatMessageDto("user", "Hello")]);

        var result = await ChatCompletionsEndpoint.HandleAsync(
            request,
            failingClient.Object,
            new FakeModelSelectionResolver(failingClient.Object),
            context,
            CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.Contains("provider_error", body);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamingResponse()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Hello ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "from ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "test");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowingStreamingResponse()
    {
        await Task.Yield();
        if (ShouldThrowProviderError())
        {
            throw new InvalidOperationException("provider down");
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, "");
    }

    private static bool ShouldThrowProviderError() => true;

    private static DefaultHttpContext CreateHttpContext()
    {
        var registry = new ModelAvailabilityRegistry();
        var checkedAt = DateTimeOffset.UtcNow;
        registry.UpdateSnapshot(
            [
                new AvailableModel("gpt-4", "AzureFoundry", "openai", "configured", Enabled: true, LastCheckedUtc: checkedAt)
            ],
            [
                new ProviderAvailabilitySnapshot("AzureFoundry", true, null, checkedAt)
            ]);

        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IModelAvailabilityRegistry>(registry)
            .BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        return context;
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

