using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Hosting;

public class CortexPortTests
{
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
