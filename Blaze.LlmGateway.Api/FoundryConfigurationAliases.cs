using Microsoft.Extensions.Configuration;

namespace Blaze.LlmGateway.Api;

public static class FoundryConfigurationAliases
{
    public static void AddFoundryEnvironmentAliases(ConfigurationManager configuration)
    {
        var aliases = new Dictionary<string, string?>();

        AddAliasIfMissing(
            configuration,
            aliases,
            "LlmGateway:Providers:AzureFoundry:Endpoint",
            "COPILOT_FOUNDRY_AZURE_BASE_URL");

        AddAliasIfMissing(
            configuration,
            aliases,
            "LlmGateway:Providers:AzureFoundry:ApiKey",
            "COPILOT_AZURE_API_KEY");

        AddAliasIfMissing(
            configuration,
            aliases,
            "LlmGateway:Providers:AzureFoundry:Model",
            "COPILOT_FOUNDRY_DEFAULT_MODEL",
            "COPILOT_FOUNDRY_GENERAL_MODEL");

        if (aliases.Count > 0)
        {
            configuration.AddInMemoryCollection(aliases);
        }
    }

    private static void AddAliasIfMissing(
        IConfiguration configuration,
        IDictionary<string, string?> aliases,
        string targetKey,
        params string[] sourceKeys)
    {
        if (!string.IsNullOrWhiteSpace(configuration[targetKey]))
        {
            return;
        }

        foreach (var sourceKey in sourceKeys)
        {
            var value = Environment.GetEnvironmentVariable(sourceKey);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            aliases[targetKey] = value;
            return;
        }
    }
}
