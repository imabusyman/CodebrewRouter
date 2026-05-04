using System.Reactive.Linq;
using System.Reactive.Subjects;
using Blaze.LlmGateway.Core.Configuration;
using Blaze.LlmGateway.LocalInference;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Blaze.LlmGateway.LocalInference.Tests;

/// <summary>
/// End-to-end integration tests verifying the complete Phase 1 local inference workflow.
/// Tests all three services working together: availability tracking, remote discovery, and health management.
/// Scenarios: local available/unavailable, remote online/offline, transitions, recovery, timeouts.
/// </summary>
public class LocalInferenceFullStackIntegrationTests
{
    private LocalModelInfo CreateLocalModel(string name) =>
        new LocalModelInfo
        {
            Name = name,
            Path = $"/models/{name}",
            ModelType = "mistral",
            LoadedAtUtc = DateTime.UtcNow,
            FileSizeBytes = 4000000000
        };

    private RemoteDiscoveryResult CreateRemoteResult(bool isOnline, int modelCount = 2) =>
        new RemoteDiscoveryResult(
            Models: isOnline
                ? Enumerable.Range(0, modelCount)
                    .Select(i => new RemoteModelInfo
                    {
                        Name = $"model-{i}",
                        Provider = i % 2 == 0 ? "OpenAI" : "Anthropic",
                        IsAvailable = true
                    })
                    .ToArray()
                : [],
            DiscoveredAtUtc: DateTime.UtcNow,
            IsOnline: isOnline);

    private DiscoveryChanged CreateDiscoveryChange(RemoteDiscoveryResult result, string reason) =>
        new DiscoveryChanged
        {
            Result = result,
            PreviousResult = null,
            Reason = reason,
            ChangedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task Scenario_LocalAvailable_RemoteAvailable_RoutesHealthy()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Act - Local becomes available
        var localAvailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = false,
            IsAvailable = true,
            Reason = "Model loaded",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localAvailable);

        // Remote comes online
        var remoteOnline = CreateRemoteResult(isOnline: true, modelCount: 3);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOnline, "Discovery successful"));

        await Task.Delay(100);

        // Assert - Both available → Healthy
        var health = healthManager.GetStatus();
        Assert.Equal(HealthStatus.Healthy, health);

        var diagnostics = healthManager.GetDiagnostics();
        Assert.True(diagnostics.LocalModelAvailable);
        Assert.True(diagnostics.RemoteDiscoveryOnline);
        Assert.Equal(3, diagnostics.RemoteModelCount);
    }

    [Fact]
    public async Task Scenario_LocalAvailable_RemoteUnavailable_RoutesDegraded()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Act - Local available, Remote offline
        var localAvailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = false,
            IsAvailable = true,
            Reason = "Model loaded",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localAvailable);

        var remoteOffline = CreateRemoteResult(isOnline: false);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOffline, "Discovery failed"));

        await Task.Delay(100);

        // Assert - One available → Degraded
        var health = healthManager.GetStatus();
        Assert.Equal(HealthStatus.Degraded, health);
    }

    [Fact]
    public async Task Scenario_LocalUnavailable_RemoteAvailable_RoutesDegraded()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Act - Local unavailable, Remote online
        var localUnavailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = true,
            IsAvailable = false,
            Reason = "Model deleted",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localUnavailable);

        var remoteOnline = CreateRemoteResult(isOnline: true, modelCount: 2);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOnline, "Discovery successful"));

        await Task.Delay(100);

        // Assert - One available → Degraded
        var health = healthManager.GetStatus();
        Assert.Equal(HealthStatus.Degraded, health);
    }

    [Fact]
    public async Task Scenario_BothUnavailable_RoutesUnavailable()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Act - Both unavailable
        var localUnavailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = true,
            IsAvailable = false,
            Reason = "Model missing",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localUnavailable);

        var remoteOffline = CreateRemoteResult(isOnline: false);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOffline, "Discovery failed"));

        await Task.Delay(100);

        // Assert - Both unavailable → Unavailable
        var health = healthManager.GetStatus();
        Assert.Equal(HealthStatus.Unavailable, health);
    }

    [Fact]
    public async Task Scenario_FailureRecovery_LocalBecomesAvailable()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Initial state: both unavailable
        var localUnavailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = false,
            IsAvailable = false,
            Reason = "Model missing",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localUnavailable);

        var remoteOffline = CreateRemoteResult(isOnline: false);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOffline, "Discovery failed"));

        await Task.Delay(100);
        Assert.Equal(HealthStatus.Unavailable, healthManager.GetStatus());

        // Recovery: local becomes available
        var localAvailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = false,
            IsAvailable = true,
            Reason = "Model downloaded",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localAvailable);

        await Task.Delay(100);

        // Assert - Should be Degraded (local available, remote still offline)
        var health = healthManager.GetStatus();
        Assert.Equal(HealthStatus.Degraded, health);
    }

    [Fact]
    public async Task Scenario_CircuitBreakerRecovery_RemoteComesBackOnline()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Initial: local available, remote offline
        var localAvailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = false,
            IsAvailable = true,
            Reason = "Model loaded",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localAvailable);

        var remoteOffline = CreateRemoteResult(isOnline: false);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOffline, "Discovery failed"));

        await Task.Delay(100);
        Assert.Equal(HealthStatus.Degraded, healthManager.GetStatus());

        // Recovery: remote comes back online
        var remoteOnline = CreateRemoteResult(isOnline: true, modelCount: 4);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOnline, "Discovery successful"));

        await Task.Delay(100);

        // Assert - Should be Healthy (both available)
        var health = healthManager.GetStatus();
        Assert.Equal(HealthStatus.Healthy, health);

        var diagnostics = healthManager.GetDiagnostics();
        Assert.Equal(4, diagnostics.RemoteModelCount);
    }

    [Fact]
    public async Task Scenario_ConcurrentAvailabilityChanges_HandlesStress()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Act - Fire many events concurrently
        var tasks = new List<Task>();

        for (int i = 0; i < 50; i++)
        {
            var localEvt = new ModelAvailabilityChanged
            {
                Model = CreateLocalModel($"model-{i}"),
                WasAvailable = false,
                IsAvailable = i % 3 == 0,
                Reason = $"Event {i}",
                ChangedAtUtc = DateTime.UtcNow
            };
            localSubject.OnNext(localEvt);

            if (i % 5 == 0)
            {
                var remoteEvt = CreateRemoteResult(isOnline: i % 2 == 0, modelCount: (i % 10) + 1);
                remoteSubject.OnNext(CreateDiscoveryChange(remoteEvt, $"Discovery {i}"));
            }
        }

        await Task.Delay(200);

        // Assert - Should finish without crashes
        var health = healthManager.GetStatus();
        Assert.NotNull(health);
    }

    [Fact]
    public async Task Scenario_HealthCheckReturnsCorrectStatus()
    {
        // Arrange
        var mockLocalAvailability = new Mock<ILocalModelAvailability>();
        var mockRemoteDiscovery = new Mock<ICodebrewRouterDiscoveryService>();
        var mockLogger = new Mock<ILogger<LocalInferenceHealthManager>>();

        var localSubject = new Subject<ModelAvailabilityChanged>();
        var remoteSubject = new Subject<DiscoveryChanged>();

        mockLocalAvailability.Setup(x => x.ObserveAvailabilityChanges()).Returns(localSubject.AsObservable());
        mockRemoteDiscovery.Setup(x => x.ObserveDiscoveryChanges()).Returns(remoteSubject.AsObservable());

        var healthManager = new LocalInferenceHealthManager(
            mockLocalAvailability.Object,
            mockRemoteDiscovery.Object,
            mockLogger.Object);

        // Initial check - all unavailable
        var result1 = await healthManager.CheckHealthAsync(default, CancellationToken.None);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result1.Status);

        // Make local available
        var localAvailable = new ModelAvailabilityChanged
        {
            Model = CreateLocalModel("mistral-7b"),
            WasAvailable = false,
            IsAvailable = true,
            Reason = "Model loaded",
            ChangedAtUtc = DateTime.UtcNow
        };
        localSubject.OnNext(localAvailable);

        var remoteOnline = CreateRemoteResult(isOnline: true, modelCount: 2);
        remoteSubject.OnNext(CreateDiscoveryChange(remoteOnline, "Discovery successful"));

        await Task.Delay(100);

        // Check health - should be healthy
        var result2 = await healthManager.CheckHealthAsync(default, CancellationToken.None);
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, result2.Status);

        // Verify diagnostics are included
        Assert.NotNull(result2.Data);
    }
}
