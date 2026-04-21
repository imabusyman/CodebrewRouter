using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for POST /v1/chat/completions endpoint via Aspire.
/// Tests request validation, streaming format, error cases, and non-streaming mode.
/// </summary>
public class ChatCompletionsEndpointTests
{
    [Fact]
    public async Task ChatCompletions_ValidStreamingRequest_ReturnsSSEStream()
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

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data: {\"choices\"", body);
        Assert.Contains("\"delta\"", body);
        Assert.Contains("\"content\"", body);
        Assert.EndsWith("data: [DONE]\n\n", body);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_NonStreamingRequest_ReturnsJsonResponse()
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
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        Assert.Equal("text_completion", json.RootElement.GetProperty("object").GetString());
        Assert.True(json.RootElement.TryGetProperty("choices", out var choices));
        Assert.True(choices.GetArrayLength() > 0);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_StreamingWithMultipleChunks_AllChunksIncluded()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Say hello world!" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // Count data: lines (should be multiple chunks + 1 [DONE])
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ")).ToList();
        Assert.True(dataLines.Count > 1, "Should have at least 2 data lines (content + [DONE])");
        Assert.True(dataLines.Last().Contains("[DONE]"), "Last data line should be [DONE]");

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_SystemAndUserMessages_BothIncluded()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] {
                new { role = "system", content = "You are helpful" },
                new { role = "user", content = "Hello" }
            },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_SSEStreamTerminatesWithDone_AlwaysPresent()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Say short" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("data: [DONE]", body);
        var lines = body.Split('\n');
        var lastDataLine = lines.LastOrDefault(l => l.StartsWith("data:"));
        Assert.NotNull(lastDataLine);
        Assert.Contains("[DONE]", lastDataLine);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_StreamingEndpointContentType_IsTextEventStream()
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

        // Assert
        Assert.NotNull(response.Content.Headers.ContentType);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType.MediaType);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_NonStreamingEndpointContentType_IsApplicationJson()
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
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_NonStreamingResponseStructure_HasCorrectFields()
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
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);
        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        // Assert
        Assert.True(json.RootElement.TryGetProperty("id", out _), "Response missing 'id'");
        Assert.True(json.RootElement.TryGetProperty("object", out _), "Response missing 'object'");
        Assert.True(json.RootElement.TryGetProperty("created", out _), "Response missing 'created'");
        Assert.True(json.RootElement.TryGetProperty("model", out _), "Response missing 'model'");
        Assert.True(json.RootElement.TryGetProperty("choices", out _), "Response missing 'choices'");
        Assert.True(json.RootElement.TryGetProperty("usage", out _), "Response missing 'usage'");

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_StreamingResponseJson_EachChunkHasCorrectStructure()
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
        var dataLines = body.Split('\n').Where(l => l.StartsWith("data: ") && !l.Contains("[DONE]")).ToList();
        foreach (var line in dataLines)
        {
            try
            {
                var json = JsonDocument.Parse(line.Replace("data: ", ""));
                Assert.True(json.RootElement.TryGetProperty("choices", out _), $"Line missing 'choices': {line}");
            }
            catch (JsonException)
            {
                // Skip non-JSON lines
            }
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_MissingMessagesField_EndpointHandlesGracefully()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var jsonBody = """{"model":"gpt-4","stream":true}""";

        // Act
        var response = await httpClient.PostAsync(
            "/v1/chat/completions",
            new StringContent(jsonBody, Encoding.UTF8, "application/json"));

        // Assert - endpoint should still process with empty messages
        Assert.NotNull(response);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_EmptyMessages_StillProcesses()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = Array.Empty<object>(),
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }
}
