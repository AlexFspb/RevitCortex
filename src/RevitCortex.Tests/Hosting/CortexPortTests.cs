using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Hosting;

public class CortexPortTests
{
    [Theory]
    [InlineData(false, 8080, 8888)]
    [InlineData(true, 8081, 8889)]
    public void ProfilePairsAndInvalidPluginOverrides_FallBackWithoutThrowing(bool dev, int first, int second)
    {
        Assert.Equal(new[] { first, second }, CortexPort.AutomaticPorts(dev));
        foreach (var invalid in new[] { "0", "-1", "65536", "oops", "8080.5" })
        {
            Assert.Equal(first, CortexPort.ResolvePlugin(invalid, dev, out var overridden, out var warning));
            Assert.False(overridden);
            Assert.Contains("REVITCORTEX_PORT", warning);
            Assert.Throws<ArgumentException>(() => CortexPort.Resolve(invalid));
        }
        Assert.Equal(first, CortexPort.ResolvePlugin(null, dev, out var defaultOverride, out var defaultWarning));
        Assert.False(defaultOverride);
        Assert.Null(defaultWarning);
        Assert.Equal(9999, CortexPort.ResolvePlugin("9999", dev, out var explicitOverride, out var explicitWarning));
        Assert.True(explicitOverride);
        Assert.Null(explicitWarning);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void DefaultClient_StaysOnPrimaryPort(string? value)
    {
        Assert.Equal(8080, CortexPort.Resolve(value));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("8080", 8080)]
    [InlineData("8888", 8888)]
    [InlineData("65535", 65535)]
    public void ExplicitClientPort_IsUsedExactly(string value, int expected)
    {
        Assert.Equal(expected, CortexPort.Resolve(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("888x")]
    [InlineData("8888.5")]
    [InlineData("999999999999999999999")]
    public void InvalidExplicitOverride_FailsInsteadOfConnectingToPrimaryPort(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => CortexPort.Resolve(value));
        Assert.Contains("REVITCORTEX_PORT", error.Message);
    }
}
