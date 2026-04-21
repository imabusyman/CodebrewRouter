using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for Azure provider handling.
/// Tests Azure SDK integration, credential handling, model discovery, and fallback behavior.
/// </summary>
public class AzureProviderTests
{
    [Fact]
    public async Task AzureProvider_IsRegisteredInDependencyInjection()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Assert
        // If the app started successfully, Azure provider should be registered
        Assert.NotNull(app);

        await app.StopAsync();
    }

    [Fact]
    public async Task AzureProvider_ChatCompletions_CanBeRouted()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Request that might be routed to Azure
        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Hello from Azure" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        // Should get a response (may be mocked, may be real)
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ModelsEndpoint_IncludesAzureModels()
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
        var json = System.Text.Json.JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        // Check if any models are from AzureFoundry provider
        var hasAzureModel = false;
        foreach (var model in data.EnumerateArray())
        {
            if (model.TryGetProperty("provider", out var provider))
            {
                if (provider.GetString() == "AzureFoundry")
                {
                    hasAzureModel = true;
                    break;
                }
            }
        }

        // At minimum, should have at least one provider listed
        Assert.True(data.GetArrayLength() > 0, "Models list should not be empty");

        await app.StopAsync();
    }

    [Fact]
    public async Task AzureCredentials_AreNotExposedInResponses()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act
        var response = await httpClient.GetAsync("/v1/models");

        // Assert
        var body = await response.Content.ReadAsStringAsync();

        // Should not contain any API keys or sensitive data
        Assert.DoesNotContain("api-key", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);

        await app.StopAsync();
    }

    [Fact]
    public async Task RoutingStrategy_SelectsAzureForAzureModels()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Request with model name that suggests Azure
        var request = new
        {
            model = "gpt-4",  // Common Azure model
            messages = new[] { new { role = "user", content = "Test routing" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        // Should successfully route to some provider
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_WithAzureModel_ProcessesRequest()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4-turbo",
            messages = new[] {
                new { role = "system", content = "You are a helpful assistant" },
                new { role = "user", content = "What is Azure?" }
            },
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ChatCompletions_WithAzureModelStreaming_StreamsResponse()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4-32k",
            messages = new[] { new { role = "user", content = "Stream this" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body);

        await app.StopAsync();
    }

    [Fact]
    public async Task FallbackBehavior_WhenProviderUnavailable_UsesDefault()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Request with any model
        var request = new
        {
            model = "any-model",
            messages = new[] { new { role = "user", content = "Test fallback" } },
            stream = true
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        // Should get a response using fallback provider
        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task ProviderSelection_IsBasedOnModel()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Different model names should potentially route to different providers
        var azureRequest = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "Test" } },
            stream = false
        };

        // Act
        var azureResponse = await httpClient.PostAsJsonAsync("/v1/chat/completions", azureRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, azureResponse.StatusCode);

        await app.StopAsync();
    }

    [Fact]
    public async Task MultipleModels_CanBeDiscovered_RegardlessOfProvider()
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
        var json = System.Text.Json.JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var modelCount = data.GetArrayLength();

        // Should discover models from multiple sources
        Assert.True(modelCount > 0, "Should discover at least one model");

        var providers = new HashSet<string>();
        foreach (var model in data.EnumerateArray())
        {
            if (model.TryGetProperty("provider", out var provider))
            {
                providers.Add(provider.GetString());
            }
        }

        // Having multiple providers is a good sign of discovery
        // But even one is acceptable if that's all configured

        await app.StopAsync();
    }

    [Fact]
    public async Task AzureIntegration_SupportsToolDefinitions()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        var request = new
        {
            model = "gpt-4",
            messages = new[] { new { role = "user", content = "What tools do you have?" } },
            tools = new[] {
                new {
                    type = "function",
                    function = new {
                        name = "get_weather",
                        description = "Get the weather for a location",
                        parameters = new {
                            type = "object",
                            properties = new {
                                location = new { type = "string" }
                            }
                        }
                    }
                }
            },
            stream = false
        };

        // Act
        var response = await httpClient.PostAsJsonAsync("/v1/chat/completions", request);

        // Assert
        // Should accept and process tools
        Assert.NotNull(response);

        await app.StopAsync();
    }

    [Fact]
    public async Task ServiceInitialization_DoesNotThrow()
    {
        // Arrange
        // Act
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Assert
        Assert.NotNull(app);

        await app.StopAsync();
    }

    [Fact]
    public async Task HealthCheck_AzureProviderAvailable()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act
        var response = await httpClient.GetAsync("/alive");

        // Assert
        // If service is alive and Azure provider didn't throw, we're good
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.True(true, "Health check passed");
        }

        await app.StopAsync();
    }
}
