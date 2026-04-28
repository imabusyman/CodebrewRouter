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

        // Aspire's Foundry deployment connection string is "Endpoint=...;Key=...;Deployment=...;Model=...".
        // DbConnectionStringBuilder cannot parse this because the URI contains colons; parse manually.
        var properties = ParseConnectionString(connectionString);

        // When the AppHost emits an Aspire-managed connection string, it always wins
        // over any stale value in appsettings.json — the Foundry Local port is dynamic
        // and only the AppHost knows the correct value for the current run.
        AddConnectionStringAliasOverride(
            aliases,
            properties,
            "LlmGateway:Providers:FoundryLocal:Endpoint",
            "Endpoint");

        AddConnectionStringAliasOverride(
            aliases,
            properties,
            "LlmGateway:Providers:FoundryLocal:ApiKey",
            "ApiKey",
            "Key");

        AddConnectionStringAliasOverride(
            aliases,
            properties,
            "LlmGateway:Providers:FoundryLocal:Model",
            "Model",
            "DeploymentId");
    }

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = segment.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = segment[..equalsIndex].Trim();
            var value = segment[(equalsIndex + 1)..].Trim();
            if (key.Length > 0 && value.Length > 0)
            {
                result[key] = value;
            }
        }
        return result;
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
        Dictionary<string, string> connectionString,
        string targetKey,
        params string[] connectionStringKeys)
    {
        if (!string.IsNullOrWhiteSpace(configuration[targetKey]))
        {
            return;
        }

        foreach (var connectionStringKey in connectionStringKeys)
        {
            if (connectionString.TryGetValue(connectionStringKey, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                aliases[targetKey] = value;
                return;
            }
        }
    }

    private static void AddConnectionStringAliasOverride(
        IDictionary<string, string?> aliases,
        Dictionary<string, string> connectionString,
        string targetKey,
        params string[] connectionStringKeys)
    {
        foreach (var connectionStringKey in connectionStringKeys)
        {
            if (connectionString.TryGetValue(connectionStringKey, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                aliases[targetKey] = value;
                return;
            }
        }
    }
}
