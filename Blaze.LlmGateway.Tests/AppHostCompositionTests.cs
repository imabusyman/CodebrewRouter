using Blaze.LlmGateway.AppHost;
using Xunit;

namespace Blaze.LlmGateway.Tests;

public class AppHostCompositionTests
{
    [Fact]
    public void Build_DoesNotThrow_WhenFoundryLocalIsEnabled()
    {
        using var app = AppHostComposition.Build([]);

        Assert.NotNull(app);
    }

    [Fact]
    public void AppHostComposition_WiresLocalInferenceWarmupEnvironment()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Blaze.LlmGateway.AppHost", "AppHostComposition.cs"));

        Assert.Contains("LlmGateway__LocalInference__ModelPath", source);
        Assert.Contains("LlmGateway__LocalInference__CacheDirectory", source);
        Assert.Contains("LlmGateway__LocalInference__DownloadTimeoutSeconds", source);
        Assert.Contains("LlmGateway__LocalInference__WarmupEnabled", source);
        Assert.Contains("LlmGateway__LocalInference__BlockStartupUntilWarm", source);
        Assert.Contains("LlmGateway__LocalInference__WarmupTimeoutSeconds", source);
    }

    [Fact]
    public void AppHostComposition_DelaysDevUiResourcesUntilApiIsReady()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Blaze.LlmGateway.AppHost", "AppHostComposition.cs"));
        var normalizedSource = source.Replace("\r\n", "\n");

        Assert.Contains(
            "builder.AddScalarApiReference()\n            .WithApiReference(api)\n            .WaitFor(api)",
            normalizedSource);
        Assert.True(
            CountOccurrences(source, ".WaitFor(api)") >= 3,
            "Scalar, OpenWebUI, and Agent DevUI should all wait for API readiness.");
    }

    [Fact]
    public void ServiceDefaults_ReadinessEndpointTreatsDegradedAsNotReady()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Blaze.LlmGateway.ServiceDefaults", "Extensions.cs"));

        Assert.Contains("ResultStatusCodes", source);
        Assert.Contains("[HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable", source);
    }

    [Fact]
    public void AppHostDefaultLocalInferenceConfig_DoesNotBlockStartupWhenModelPathIsEmpty()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Blaze.LlmGateway.AppHost", "appsettings.json"));

        Assert.Contains("\"ModelPath\": \"\"", source);
        Assert.Contains("\"BlockStartupUntilWarm\": false", source);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Blaze.LlmGateway.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
