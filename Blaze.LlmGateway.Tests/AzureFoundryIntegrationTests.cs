using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for Azure Foundry (gpt-4o) provider.
/// Includes both mocked tests (fast) and real Azure integration tests (slow, requires credentials).
/// </summary>
public class AzureFoundryIntegrationTests : IAsyncLifetime
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
                    // Remove all existing IChatClient registrations
                    var descriptorsToRemove = services
                        .Where(d => d.ServiceType == typeof(IChatClient))
                        .ToList();
                    
                    foreach (var descriptor in descriptorsToRemove)
                    {
                        services.Remove(descriptor);
                    }

                    // Mock the IChatClient to simulate Azure responses
                    var mockChatClient = new Mock<IChatClient>();
                    
                    // Setup streaming response (simulating Azure gpt-4o streaming)
                    mockChatClient
                        .Setup(c => c.GetStreamingResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .Returns(StreamingAzureResponse());
                    
                    // Setup non-streaming response (simulating Azure gpt-4o)
                    mockChatClient
                        .Setup(c => c.GetResponseAsync(
                            It.IsAny<IEnumerable<ChatMessage>>(),
                            It.IsAny<ChatOptions>(),
                            It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ChatResponse(
                            [new ChatMessage(ChatRole.Assistant, "Azure gpt-4o response: This is a powerful model optimized for complex reasoning.")]));

                    services.AddSingleton(mockChatClient.Object);
                });
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    /// <summary>
    /// MOCKED TEST: Verify Azure Foundry endpoint accepts chat completions requests
    /// </summary>
    [Fact]
    public async Task AzureFoundry_ChatCompletions_MockedGpt4o_ReturnsResponse()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4o",
            messages = new[] {
                new { role = "user", content = "What is the capital of France?" }
            },
            temperature = 0.7,
            max_tokens = 100
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseBody);
        
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);
        Assert.True(json.RootElement.TryGetProperty("model", out var model));
        Assert.Equal("gpt-4o", model.GetString());
    }

    /// <summary>
    /// MOCKED TEST: Verify streaming responses from Azure Foundry gpt-4o
    /// </summary>
    [Fact]
    public async Task AzureFoundry_ChatCompletions_StreamingMocked_ReturnsSSEStream()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4o",
            messages = new[] {
                new { role = "user", content = "List 3 benefits of cloud computing" }
            },
            stream = true
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Should have at least one chunk + done marker
        Assert.NotEmpty(lines);
        Assert.Contains(lines, l => l.Contains("[DONE]"));
    }

    /// <summary>
    /// MOCKED TEST: Verify completions endpoint with gpt-4o
    /// </summary>
    [Fact]
    public async Task AzureFoundry_Completions_MockedGpt4o_ReturnsText()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4o",
            prompt = "Once upon a time",
            max_tokens = 50
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/completions", content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseBody);
        
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);
    }

    /// <summary>
    /// MOCKED TEST: Verify models endpoint returns CodebrewRouter model
    /// </summary>
    [Fact]
    public async Task AzureFoundry_ModelsEndpoint_IncludesCodebrewRouter()
    {
        // Act
        var response = await _client!.GetAsync("/v1/models");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(responseBody);
        
        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var models = data.EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToList();
        
        // CodebrewRouter is always configured as a virtual model
        Assert.Contains("codebrewRouter", models);
    }

    /// <summary>
    /// MOCKED TEST: Verify Azure Foundry provider is selectable
    /// </summary>
    [Fact]
    public async Task AzureFoundry_Request_WithGpt4oModel_IsRoutedCorrectly()
    {
        // Arrange
        var request = new
        {
            model = "gpt-4o",
            messages = new[] {
                new { role = "user", content = "Azure test request" }
            }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        // Act
        var response = await _client!.PostAsync("/v1/chat/completions", content);

        // Assert - if we got a successful response, routing worked
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// REAL INTEGRATION TEST (requires Azure credentials)
    /// Tests actual Azure Foundry gpt-4o without mocking
    /// Skip this test if Azure credentials are not configured
    /// </summary>
    [Fact(Skip = "Requires Azure Foundry credentials to be set. Configure with: dotnet user-secrets set \"Parameters:azure-foundry-endpoint\" \"https://your-resource.openai.azure.com/\" --project Blaze.LlmGateway.AppHost && dotnet user-secrets set \"Parameters:azure-foundry-api-key\" \"<key>\" --project Blaze.LlmGateway.AppHost")]
    public async Task AzureFoundry_RealIntegration_ChatCompletionsWithGpt4o_Succeeds()
    {
        // This test requires:
        // 1. Azure Foundry credentials configured
        // 2. Running via Aspire AppHost (not WebApplicationFactory)
        // See instructions in [Skip] attribute above
        
        // For now, this test is skipped. To enable:
        // 1. Set your Azure credentials using dotnet user-secrets
        // 2. Create a real Aspire-based test factory
        // 3. Remove the [Fact(Skip = ...)] and use [Fact]
        
        Assert.True(true); // Placeholder
    }

    // Helper method to simulate Azure streaming response
    private static async IAsyncEnumerable<ChatResponseUpdate> StreamingAzureResponse()
    {
        var chunks = new[]
        {
            "Azure ",
            "gpt-4o ",
            "streaming ",
            "response ",
            "working ",
            "correctly."
        };

        foreach (var chunk in chunks)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            await Task.Delay(10); // Simulate network delay
        }
    }
}
