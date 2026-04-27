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
}
