using Microsoft.Extensions.Configuration;
using System.Data.Common;

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
            "LlmGateway:Providers:AzureFoundry:ResponsesEndpoint",
            "COPILOT_FOUNDRY_RESPONSES_ENDPOINT");

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

        AddFoundryLocalConnectionStringAliases(configuration, aliases);

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

    private static void AddFoundryLocalConnectionStringAliases(
        IConfiguration configuration,
        IDictionary<string, string?> aliases)
    {
        var connectionString = GetFirstConnectionString(configuration, "foundryLocalChat", "foundryLocal");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        AddConnectionStringAliasIfMissing(
            configuration,
            aliases,
            builder,
            "LlmGateway:Providers:FoundryLocal:Endpoint",
            "Endpoint");

        AddConnectionStringAliasIfMissing(
            configuration,
            aliases,
            builder,
            "LlmGateway:Providers:FoundryLocal:ApiKey",
            "ApiKey");
    }

    private static string? GetFirstConnectionString(IConfiguration configuration, params string[] names)
    {
        foreach (var name in names)
        {
            var value =
                configuration.GetConnectionString(name) ??
                configuration[$"ConnectionStrings:{name}"] ??
                Environment.GetEnvironmentVariable($"ConnectionStrings__{name}");

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static void AddConnectionStringAliasIfMissing(
        IConfiguration configuration,
        IDictionary<string, string?> aliases,
        DbConnectionStringBuilder connectionString,
        string targetKey,
        string connectionStringKey)
    {
        if (!string.IsNullOrWhiteSpace(configuration[targetKey]))
        {
            return;
        }

        if (connectionString.TryGetValue(connectionStringKey, out var value) &&
            value is string stringValue &&
            !string.IsNullOrWhiteSpace(stringValue))
        {
            aliases[targetKey] = stringValue;
        }
    }
}
