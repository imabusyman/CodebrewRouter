using Blaze.LlmGateway.Api;
using Blaze.LlmGateway.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.Tests;

public class LlmGatewayOptionsTests
{
    [Fact]
    public void OllamaLocalOptions_DefaultsToRemoteGemmaRouter()
    {
        var options = new LlmGatewayOptions();

        Assert.Equal("http://192.168.16.12:11434", options.Providers.OllamaLocal.BaseUrl);
        Assert.Equal("gemma4:e4b", options.Providers.OllamaLocal.Model);
    }

    [Fact]
    public void FoundryLocalOptions_DefaultsToLoadedPhiMiniModel()
    {
        var options = new LlmGatewayOptions();

        Assert.Equal("http://127.0.0.1:58484", options.Providers.FoundryLocal.Endpoint);
        Assert.Equal("Phi-4-mini-instruct-cuda-gpu:5", options.Providers.FoundryLocal.Model);
    }

    [Fact]
    public void FoundryConfigurationAliases_MapsCopilotEnvironmentVariables_WhenGatewayKeysAreMissing()
    {
        const string endpoint = "https://example-foundry.openai.azure.com/";
        const string apiKey = "test-api-key";
        const string model = "gpt-4o-test";

        Environment.SetEnvironmentVariable("COPILOT_FOUNDRY_AZURE_BASE_URL", endpoint);
        Environment.SetEnvironmentVariable("COPILOT_AZURE_API_KEY", apiKey);
        Environment.SetEnvironmentVariable("COPILOT_FOUNDRY_DEFAULT_MODEL", model);

        try
        {
            var configuration = new ConfigurationManager();

            Blaze.LlmGateway.Api.FoundryConfigurationAliases.AddFoundryEnvironmentAliases(configuration);

            Assert.Equal(endpoint, configuration["LlmGateway:Providers:AzureFoundry:Endpoint"]);
            Assert.Equal(apiKey, configuration["LlmGateway:Providers:AzureFoundry:ApiKey"]);
            Assert.Equal(model, configuration["LlmGateway:Providers:AzureFoundry:Model"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_FOUNDRY_AZURE_BASE_URL", null);
            Environment.SetEnvironmentVariable("COPILOT_AZURE_API_KEY", null);
            Environment.SetEnvironmentVariable("COPILOT_FOUNDRY_DEFAULT_MODEL", null);
        }
    }

    [Fact]
    public void FoundryConfigurationAliases_DoesNotOverrideExplicitGatewayKeys()
    {
        Environment.SetEnvironmentVariable("COPILOT_FOUNDRY_AZURE_BASE_URL", "https://alias.example/");

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LlmGateway:Providers:AzureFoundry:Endpoint"] = "https://explicit.example/"
            });

            Blaze.LlmGateway.Api.FoundryConfigurationAliases.AddFoundryEnvironmentAliases(configuration);

            Assert.Equal("https://explicit.example/", configuration["LlmGateway:Providers:AzureFoundry:Endpoint"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_FOUNDRY_AZURE_BASE_URL", null);
        }
    }

    [Fact]
    public async Task ModelCatalogService_IncludesConfiguredCodebrewBackingProviders()
    {
        var options = new LlmGatewayOptions
        {
            Providers =
            {
                GithubModels = { ApiKey = "test-token" }
            }
        };
        var service = CreateModelCatalogService(options);

        var models = await service.GetAvailableModelsAsync();

        Assert.Contains(models, model => model.Provider == "CodebrewRouter" && model.Id == "codebrewRouter");
        Assert.Contains(models, model => model.Provider == "FoundryLocal" && model.Id == "Phi-4-mini-instruct-cuda-gpu:5");
        Assert.Contains(models, model => model.Provider == "OllamaLocal" && model.Id == "gemma4:e4b");
        Assert.Contains(models, model => model.Provider == "GithubModels" && model.Id == "gpt-4o-mini");
    }

    [Fact]
    public async Task ModelCatalogService_SkipsGithubModels_WhenApiKeyMissing()
    {
        var service = CreateModelCatalogService(new LlmGatewayOptions());

        var models = await service.GetAvailableModelsAsync();

        Assert.DoesNotContain(models, model => model.Provider == "GithubModels");
    }

    private static ModelCatalogService CreateModelCatalogService(LlmGatewayOptions options)
    {
        var discovery = new AzureFoundryModelDiscovery(
            new HttpClient(),
            new Mock<ILogger<AzureFoundryModelDiscovery>>().Object);

        return new ModelCatalogService(
            Options.Create(options),
            discovery,
            new Mock<ILogger<ModelCatalogService>>().Object);
    }
}
