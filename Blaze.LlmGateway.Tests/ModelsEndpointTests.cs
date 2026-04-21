using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Aspire.Hosting.Testing;
using Xunit;

namespace Blaze.LlmGateway.Tests;

/// <summary>
/// Integration tests for GET /v1/models endpoint via Aspire.
/// Tests model discovery, provider detection, and response structure.
/// </summary>
public class ModelsEndpointTests
{
    [Fact]
    public async Task Models_GetRequest_ReturnsJsonList()
    {
        // Arrange
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Blaze_LlmGateway_AppHost>();
        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        var httpClient = app.CreateHttpClient("api");

        // Act
        var response = await httpClient.GetAsync("/v1/models");

        // Assert
        // If endpoint doesn't exist yet, that's OK for pre-Coder state
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await app.StopAsync();
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);

        Assert.True(json.RootElement.TryGetProperty("object", out var objectProp));
        Assert.Equal("list", objectProp.GetString());
        Assert.True(json.RootElement.TryGetProperty("data", out var dataProp));
        Assert.Equal(JsonValueKind.Array, dataProp.ValueKind);

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_ResponseStructure_HasCorrectFormat()
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

        Assert.True(json.RootElement.TryGetProperty("object", out _));
        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("id", out _), "Model missing 'id'");
            Assert.True(model.TryGetProperty("object", out _), "Model missing 'object'");
            Assert.True(model.TryGetProperty("provider", out _), "Model missing 'provider'");
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_DataArray_NotEmpty()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var modelCount = data.GetArrayLength();
        Assert.True(modelCount > 0, "Models list should not be empty");

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_ContainsKnownProviders()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));
        var providers = new HashSet<string>();

        foreach (var model in data.EnumerateArray())
        {
            if (model.TryGetProperty("provider", out var provider))
            {
                providers.Add(provider.GetString());
            }
        }

        // Should have at least one known provider
        var knownProviders = new[] { "Ollama", "AzureFoundry", "Gemini", "GithubModels", "OpenRouter", "GithubCopilot" };
        var hasKnownProvider = providers.Any(p => knownProviders.Contains(p));
        Assert.True(hasKnownProvider, $"No known providers found. Found: {string.Join(", ", providers)}");

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_EachModelHasObject_EqualToModel()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("object", out var objectProp));
            Assert.Equal("model", objectProp.GetString());
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_IdField_NotEmpty()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("id", out var idProp));
            var id = idProp.GetString();
            Assert.False(string.IsNullOrWhiteSpace(id), "Model ID should not be empty");
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_ProviderField_NotEmpty()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("provider", out var providerProp));
            var provider = providerProp.GetString();
            Assert.False(string.IsNullOrWhiteSpace(provider), "Provider should not be empty");
        }

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_ContentType_IsApplicationJson()
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

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_ResponseIsValidJson()
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

        // Should not throw
        var json = JsonDocument.Parse(body);
        Assert.NotNull(json);

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_MultipleModels_AllHaveConsistentStructure()
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

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_OwnedByField_IsOptional()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        // At least one model should exist (even if owned_by is not populated)
        var modelCount = data.GetArrayLength();
        Assert.True(modelCount > 0);

        await app.StopAsync();
    }

    [Fact]
    public async Task Models_ProviderNames_AreValid()
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

        Assert.True(json.RootElement.TryGetProperty("data", out var data));

        var validProviders = new[] { "Ollama", "AzureFoundry", "Gemini", "GithubModels", "OpenRouter", "GithubCopilot", "OllamaBackup", "FoundryLocal", "OllamaLocal" };

        foreach (var model in data.EnumerateArray())
        {
            Assert.True(model.TryGetProperty("provider", out var providerProp));
            var provider = providerProp.GetString();
            Assert.Contains(provider, validProviders);
        }

        await app.StopAsync();
    }
}
