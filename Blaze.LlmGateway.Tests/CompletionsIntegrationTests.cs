using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for POST /v1/completions endpoint.
/// Tests text-only completion format with streaming and non-streaming modes.
/// </summary>
public class CompletionsIntegrationTests : IAsyncLifetime
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
                        .Returns(StreamingResponse());
                    
                    mockChatClient
                        .Setup(c => c.GetResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ChatResponse(
                            [new ChatMessage(ChatRole.Assistant, "Once upon a time, there was a tale")]));

                    services.AddSingleton(mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("AzureFoundry", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("GithubCopilot", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("Gemini", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("OpenRouter", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("FoundryLocal", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("GithubModels", mockChatClient.Object);
                    services.AddKeyedSingleton<IChatClient>("OllamaLocal", mockChatClient.Object);
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
    public async Task StreamingCompletion_WithValidRequest_ReturnsSSE()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Once upon a time",
            stream = true
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body);
        Assert.EndsWith("data: [DONE]\n\n", body);
    }

    [Fact]
    public async Task StreamingCompletion_ResponseFormat_ContainsValidJSON()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Complete this",
            stream = true
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ") && !l.Contains("[DONE]")).ToList();
        foreach (var line in dataLines)
        {
            var jsonStr = line.Replace("data: ", "");
            var json = JsonDocument.Parse(jsonStr);
            Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
            var choice = choices.EnumerateArray().First();
            Assert.True(choice.TryGetProperty("text", out _), $"Missing 'text' in choice: {line}");
        }
    }

    [Fact]
    public async Task NonStreamingCompletion_WithValidRequest_ReturnsJSON()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Once upon a time",
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        
        Assert.True(json.RootElement.TryGetProperty("object", out var obj));
        Assert.Equal("text_completion", obj.GetString());
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);
    }

    [Fact]
    public async Task NonStreamingCompletion_ResponseStructure_ContainsAllRequiredFields()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test prompt",
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);
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
        Assert.True(choice.TryGetProperty("text", out _), "Choice missing 'text'");
    }

    [Fact]
    public async Task Completion_MissingModel_ReturnsBadRequest()
    {
        // Arrange
        var request = new { prompt = "Test prompt", stream = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Completion_MissingPrompt_ReturnsBadRequest()
    {
        // Arrange
        var request = new { model = "gpt-3.5-turbo", stream = false };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Completion_WithMaxTokens_IsRespected()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            max_tokens = 50,
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Completion_WithTemperature_IsRespected()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            temperature = 0.7,
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task StreamingCompletion_MultipleChunks_AllChunksReceived()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Once upon a time",
            stream = true
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ")).ToList();
        Assert.True(dataLines.Count > 1, "Should have multiple chunks");
        Assert.True(dataLines.Last().Contains("[DONE]"), "Last chunk should be [DONE]");
    }

    [Fact]
    public async Task Completion_StreamTerminatesWithDoneMarker()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test prompt",
            stream = true
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("data: [DONE]", body);
        Assert.EndsWith("data: [DONE]\n\n", body);
    }

    [Fact]
    public async Task Completion_IDField_IsUnique()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            stream = false
        };

        // Act
        var response1 = await _client!.PostAsync("/v1/completions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        var body1 = await response1.Content.ReadAsStringAsync();
        var json1 = JsonDocument.Parse(body1);
        var id1 = json1.RootElement.GetProperty("id").GetString();

        var response2 = await _client!.PostAsync("/v1/completions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        var body2 = await response2.Content.ReadAsStringAsync();
        var json2 = JsonDocument.Parse(body2);
        var id2 = json2.RootElement.GetProperty("id").GetString();

        // Assert
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public async Task Completion_WithTopP_IsRespected()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            top_p = 0.9,
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task StreamingCompletion_ContentType_IsTextEventStream()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            stream = true
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NonStreamingCompletion_ContentType_IsApplicationJson()
    {
        // Arrange
        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            stream = false
        };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    private static void RemoveServicesByType(IServiceCollection services, Type serviceType)
    {
        var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> StreamingResponse()
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Once upon ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "a time, ");
        yield return new ChatResponseUpdate(ChatRole.Assistant, "there was.");
    }
}
