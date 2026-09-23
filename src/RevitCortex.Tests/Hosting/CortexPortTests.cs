using RevitCortex.Core.Hosting;
using Xunit;

namespace RevitCortex.Tests.Hosting;

public sealed class CortexPortTests : IDisposable
{
    private readonly string _settings = Path.Combine(Path.GetTempPath(), $"cortex-port-{Guid.NewGuid():N}.json");

    [Fact]
    public void ProcessPorts_StayIndependentOfSharedSettings_AndNeverPersist()
    {
        const string original = "{\"Port\":8080,\"ReadOnlyMode\":true,\"DisabledTools\":[\"delete_elements\"]}";
        File.WriteAllText(_settings, original);
        Assert.Equal(8888, CortexPort.Resolve("8888", _settings));
        Assert.Equal(8889, CortexPort.Resolve("8889", _settings));
        Assert.Equal(original, File.ReadAllText(_settings));

        File.WriteAllText(_settings, "{\"Port\":9000}");
        Assert.Equal(8888, CortexPort.Resolve("8888", _settings));
        Assert.Equal(8889, CortexPort.Resolve("8889", _settings));
        Assert.Equal(9000, CortexPort.Resolve(null, _settings));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NoOverride_UsesExistingSettings(string? value)
    {
        File.WriteAllText(_settings, "{\"Port\":9999}");
        Assert.Equal(9999, CortexPort.Resolve(value, _settings));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void Override_AcceptsPortBoundaries(string value, int port)
    {
        Assert.Equal(port, CortexPort.Resolve(value, _settings));
        Assert.False(File.Exists(_settings));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("888x")]
    [InlineData("8888.5")]
    [InlineData("999999999999999999999")]
    public void InvalidExplicitOverride_FailsInsteadOfConnectingToSharedPort(string value)
    {
        File.WriteAllText(_settings, "{\"Port\":8080}");
        var error = Assert.Throws<ArgumentException>(() => CortexPort.Resolve(value, _settings));
        Assert.Contains("REVITCORTEX_PORT", error.Message);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"Port\":0}")]
    [InlineData("{\"Port\":65536}")]
    [InlineData("{\"Port\":\"bad\"}")]
    public void InvalidLegacySettings_KeepProfileDefault(string json)
    {
        File.WriteAllText(_settings, json);
        Assert.Equal(8081, CortexPort.Resolve(null, _settings, 8081));
        Assert.Equal(8888, CortexPort.Resolve("8888", _settings, 8081));
    }

    [Fact]
    public void MissingSettings_KeepProfileDefault()
    {
        Assert.Equal(8080, CortexPort.Resolve(null, _settings));
        Assert.Equal(8081, CortexPort.Resolve(null, _settings, 8081));
    }

    public void Dispose() => File.Delete(_settings);
}
