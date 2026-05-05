namespace Blaze.LlmGateway.Tests.Provider;

using Blaze.LlmGateway.Core;
using Blaze.LlmGateway.Core.Provider;
using Blaze.LlmGateway.Infrastructure.Provider;
using Blaze.LlmGateway.Infrastructure.RoutingStrategies;
using Blaze.LlmGateway.LocalInference;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class CodebrewRouterProviderIntegrationTests
{
    private CodebrewRouterProviderOptions CreateValidOptions()
        => new()
        {
            LocalEndpoint = "http://localhost:11434",
            RemoteDiscoveryEndpoint = "http://localhost:5273",
            HealthChecksEnabled = true,
            TestMode = false
        };

    [Fact]
    public void Mobile_Scenario_MinimalConfiguration_Works()
    {
        // Arrange: Mobile (MAUI) with just LocalEndpoint
        var services = new ServiceCollection();
        var options = new CodebrewRouterProviderOptions
        {
            LocalEndpoint = "http://192.168.1.100:11434"
        };

        // Act: Simple registration (no builder)
        services.AddCodebrewRouterProvider(options);
        var sp = services.BuildServiceProvider();

        // Assert: Core services available
        var chatClient = sp.GetRequiredService<IChatClient>();
        Assert.NotNull(chatClient);

        var availability = sp.GetRequiredService<ILocalModelAvailability>();
        Assert.NotNull(availability);
    }

    [Fact]
    public void Desktop_Scenario_FullStack_Works()
    {
        // Arrange: Desktop with full feature stack
        var services = new ServiceCollection();
        var options = CreateValidOptions();

        // Act: Builder chain
        var builder = services
            .AddCodebrewRouterProvider(options)
            .WithHealthChecks()
            .WithDiscovery()
            .WithRouting();

        var sp = builder.Build().BuildServiceProvider();

        // Assert: All services registered
        var chatClient = sp.GetRequiredService<IChatClient>();
        var availability = sp.GetRequiredService<ILocalModelAvailability>();
        var discovery = sp.GetRequiredService<ICodebrewRouterDiscoveryService>();
        var healthManager = sp.GetRequiredService<ILocalInferenceHealthManager>();
        var strategy = sp.GetService<IRoutingStrategy>();

        Assert.NotNull(chatClient);
        Assert.NotNull(availability);
        Assert.NotNull(discovery);
        Assert.NotNull(healthManager);
        Assert.NotNull(strategy);
    }

    [Fact]
    public void Aspire_Scenario_WithConfiguration_Works()
    {
        // Arrange: Aspire with IConfiguration binding
        var services = new ServiceCollection();
        var options = CreateValidOptions();

        // Act: Use new provider directly (old API would use config binding)
        services
            .AddCodebrewRouterProvider(options)
            .WithHealthChecks()
            .WithDiscovery()
            .Build();

        var sp = services.BuildServiceProvider();

        // Assert: Aspire health checks available
        var healthManager = sp.GetRequiredService<ILocalInferenceHealthManager>();
        Assert.NotNull(healthManager);
    }

    [Fact]
    public void CustomRoutingStrategy_CanBeRegistered()
    {
        // Arrange: Custom strategy
        var services = new ServiceCollection();
        var options = CreateValidOptions();

        // Act: Register custom strategy
        services
            .AddCodebrewRouterProvider(options)
            .WithRoutingStrategy<CustomTestRoutingStrategy>()
            .Build();

        var sp = services.BuildServiceProvider();

        // Assert: Custom strategy is used
        var strategy = sp.GetRequiredService<IRoutingStrategy>();
        Assert.IsType<CustomTestRoutingStrategy>(strategy);
    }

    [Fact]
    public void HealthCheckDisabled_InOptions_SkipsHealthCheck()
    {
        // Arrange: Options with health check disabled
        var services = new ServiceCollection();
        var options = new CodebrewRouterProviderOptions
        {
            LocalEndpoint = "http://localhost:11434",
            HealthChecksEnabled = false
        };

        // Act
        services.AddCodebrewRouterProvider(options);
        var sp = services.BuildServiceProvider();

        // Assert: Health manager still registered, but check skipped
        var healthManager = sp.GetRequiredService<ILocalInferenceHealthManager>();
        Assert.NotNull(healthManager);
    }

    [Fact]
    public void DegradedState_OnMobileWithoutRemoteDiscovery_AllowsFunctionality()
    {
        // Arrange: Mobile with only local endpoint
        var services = new ServiceCollection();
        var options = new CodebrewRouterProviderOptions
        {
            LocalEndpoint = "http://localhost:11434",
            RemoteDiscoveryEndpoint = null  // No remote
        };

        // Act
        var builder = services.AddCodebrewRouterProvider(options);
        var sp = builder.Build().BuildServiceProvider();

        // Assert: App starts in degraded but functional state
        var chatClient = sp.GetRequiredService<IChatClient>();
        var healthManager = sp.GetRequiredService<ILocalInferenceHealthManager>();
        
        Assert.NotNull(chatClient);
        Assert.NotNull(healthManager);
    }

    /// <summary>
    /// Test-only routing strategy for validation.
    /// </summary>
    private class CustomTestRoutingStrategy : IRoutingStrategy
    {
        public Task<RouteDestination> ResolveAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(RouteDestination.LocalGemma);
    }
}
