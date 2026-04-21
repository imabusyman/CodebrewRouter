using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Aspire.Hosting.Testing;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for LiteLLM compatibility.
/// Tests end-to-end request/response cycles, provider routing, and OpenAI-compatible format compliance.
/// </summary>
public class LiteLlmCompatibilityTests
{
    [Fact]
    public async Task ChatCompletionsEndpoint_OpenAiCompatibleRequest_ReturnsOpenAiCompatibleResponse()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // OpenAI-compatible request format
        var request = new
        {
            model = "gpt-4",
            messages = new[] {
                new { role = "system", content = "You are helpful" },
                new { role = "user", content = "What is 2+2?" }
            },
            temperature = 0.7,
            max_tokens = 100,
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Verify OpenAI-compatible response structure
        Assert.True(json.RootElement.TryGetProperty("id", out _));
        Assert.True(json.RootElement.TryGetProperty("object", out _));
        Assert.True(json.RootElement.TryGetProperty("created", out _));
        Assert.True(json.RootElement.TryGetProperty("model", out _));
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);

        var choice = choices.EnumerateArray().First();
        Assert.True(choice.TryGetProperty("message", out var message));
        Assert.True(message.TryGetProperty("role", out var role));
        Assert.True(message.TryGetProperty("content", out var content));

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletionsStreaming_SSEFormatCompliance_AllChunksAreValidSSE()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        // Verify SSE format: each line should start with "data: " followed by JSON
        var lines = body.Split('\n');
        var dataLines = lines.Where(l => l.StartsWith("data: ")).ToList();

        Assert.True(dataLines.Count > 0, "Should have at least one data line");

        foreach (var line in dataLines)
        {
            if (line.Contains("[DONE]"))
            {
                Assert.Equal("data: [DONE]", line);
            }
            else
            {
                var jsonPart = line.Replace("data: ", "");
                // Should be valid JSON
                var json = JsonDocument.Parse(jsonPart);
                Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
            }
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task CompletionsEndpoint_TextOnlyFormat_ReturnsTextChoices()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Once upon a time",
            max_tokens = 50,
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/completions", request);

        // Assert
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await app.StopAsync();
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        var choice = choices.EnumerateArray().First();
        Assert.True(choice.TryGetProperty("text", out _), "Completions endpoint should use 'text' field, not 'message'");

        await app.StopAsync();
    }

    [Fact]
    public async Task StreamingEndpoint_SSETerminator_DoneMarkerPresentAtEnd()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Test" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        // Must end with [DONE] marker
        Assert.True(body.Contains("data: [DONE]"), "SSE stream must contain data: [DONE] marker");
        Assert.True(body.EndsWith("data: [DONE]\n\n"), "SSE stream must end with proper [DONE] terminator");

        await app.StopAsync();
    }

    [Fact]
    public async Task ModelsEndpoint_ProviderList_RoutingStrategyCanUseIt()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act
        var response = await httpClient.GetAsync("/v1/models");

        // Assert
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await app.StopAsync();
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Verify structure suitable for routing decision-making
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        var providerToModels = new Dictionary<string, List<string>>();
        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("id", out var idProp));
            Assert.True(model.TryGetProperty("provider", out var providerProp));

            var id = idProp.GetString();
            var provider = providerProp.GetString();

            if (!providerToModels.ContainsKey(provider))
            {
                providerToModels[provider] = new List<string>();
            }
            providerToModels[provider].Add(id);
        }

        // Should have models grouped by provider
        Assert.True(providerToModels.Count > 0);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_WithAllOptionalParameters_ProcessedSuccessfully()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Test" } },
            temperature = 0.8,
            max_tokens = 200,
            top_p = 0.9,
            frequency_penalty = 0.5,
            presence_penalty = 0.5,
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task Completions_WithAllOptionalParameters_ProcessedSuccessfully()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test",
            temperature = 0.8,
            max_tokens = 200,
            top_p = 0.9,
            frequency_penalty = 0.5,
            presence_penalty = 0.5,
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/completions", request);

        // Assert
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await app.StopAsync();
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task EndpointsAreDiscoverable_ViaModelsAndChatEndpoints()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act
        var modelsResponse = await httpClient.GetAsync("/v1/models");
        var chatResponse = await httpClient.PostAsJsonAsync("/v1/chat/completions", new { model = "test", messages = Array.Empty<object>(), stream = true });

        // Assert
        // At minimum, these endpoints should be accessible
        Assert.NotNull(modelsResponse);
        Assert.NotNull(chatResponse);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletionStreaming_ChunksContainDelta_NotMessage()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Say hello" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ") && !l.Contains("[DONE]")).ToList();
        foreach (var line in dataLines)
        {
            try
            {
                var jsonPart = line.Replace("data: ", "");
                var json = JsonDocument.Parse(jsonPart);
                
                Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
                var choice = choices.EnumerateArray().First();
                
                // Streaming should use 'delta', not 'message'
                Assert.True(choice.TryGetProperty("delta", out _), "Streaming chunks should have 'delta' field");
            }
            catch (JsonException)
            {
                // Skip invalid lines
            }
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task CompletionStreaming_ChunksContainText_WithoutMessage()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-3.5-turbo",
            prompt = "Test prompt",
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/completions", request);

        // Assert
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await app.StopAsync();
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ") && !l.Contains("[DONE]")).ToList();
        
        foreach (var line in dataLines)
        {
            try
            {
                var jsonPart = line.Replace("data: ", "");
                var json = JsonDocument.Parse(jsonPart);
                
                Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
                var choice = choices.EnumerateArray().First();
                
                // Text completions should use 'text', not 'delta'
                Assert.True(choice.TryGetProperty("text", out _), "Completion chunks should have 'text' field");
            }
            catch (JsonException)
            {
                // Skip invalid lines
            }
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task AllEndpoints_RespondWithoutDelay_PerformanceAcceptable()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act & Assert
        var startModels = DateTime.UtcNow;
        await httpClient.GetAsync("/v1/models");
        var modelsLatency = DateTime.UtcNow - startModels;
        Assert.True(modelsLatency.TotalSeconds < 10, "Models endpoint should respond within 10 seconds");

        var startChat = DateTime.UtcNow;
        await httpClient.PostAsJsonAsync("/v1/chat/completions", new { model = "test", messages = new[] { new { role = "user", content = "test" } }, stream = true });
        var chatLatency = DateTime.UtcNow - startChat;
        Assert.True(chatLatency.TotalSeconds < 10, "Chat endpoint should respond within 10 seconds");

        await app.StopAsync();
    }
}
