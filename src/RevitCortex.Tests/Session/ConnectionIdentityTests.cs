using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Session;
using RevitCortex.Core.Discovery;
using RevitCortex.Plugin;
using Xunit;
namespace RevitCortex.Tests.Session;
public class ConnectionIdentityTests
{
    [Fact]
    public void CachedStatusRespectsDisabledToolSetting()
    {
        var router = new CortexRouter(new CortexSession(new SessionStore()), new Router.FakeAnalyzer());
        router.RegisterTool(new RevitCortex.Tools.Meta.GetConnectionStatusTool());
        router.SetDisabledTools(new[] { "get_connection_status" });
        var response = router.Route("get_connection_status", new JObject());
        Assert.False(response.Success);
        Assert.Contains("disabled", response.Error!.Message);
    }

    [Fact]
    public void SnapshotWorksWithoutDocumentAndNeverContainsNativeObject()
    {
        var s = new CortexSession(new SessionStore()) { BridgePort = 8082 };
        var router = new CortexRouter(s, new Router.FakeAnalyzer());
        router.RegisterTool(new RevitCortex.Tools.Meta.GetConnectionStatusTool());
        var response = router.Route("get_connection_status", new JObject());
        Assert.True(response.Success);
        var status = Assert.IsType<JObject>(response.Data);
        Assert.Equal(8082, status.Value<int>("bridgePort"));
        Assert.False(status.Value<bool>("documentPresent"));
        Assert.True(status.Value<bool>("snapshotOnly"));
        Assert.True(status.Value<int>("revitProcessId") > 0);
        Assert.Equal("RevitCortex/1", status.Value<string>("protocol"));
    }

    [Fact]
    public void IdentityInvalidatesOnDocumentSwitchAndProcessRestart()
    {
        var s = new CortexSession(new SessionStore()) { BridgePort = 8080 };
        s.Reinitialize(new DocumentCapabilities(), "en", new object());
        s.UpdateDocumentTitle("A"); s.UpdateDocumentMetadata("A.rvt", true);
        var identity = s.ConnectionStatus();
        Assert.True(s.MatchesTarget(identity));
        Assert.True(identity.Value<bool>("isServiceDocument"));
        s.Reinitialize(new DocumentCapabilities(), "en", new object());
        Assert.False(s.MatchesTarget(identity));
        Assert.False(s.ConnectionStatus().Value<bool>("isServiceDocument"));
        var replacement = new CortexSession(new SessionStore()) { BridgePort = 8080 };
        Assert.False(replacement.MatchesTarget(identity));
        Assert.NotEqual(s.InstanceId, replacement.InstanceId);
    }

    [Theory]
    [InlineData("instanceId")]
    [InlineData("bridgePort")]
    [InlineData("documentGeneration")]
    public void WrongIdentityIsRejectedBeforeToolLookupOrCache(string field)
    {
        var s = new CortexSession(new SessionStore()) { BridgePort = 8080 };
        var expected = s.ConnectionStatus(); expected.Remove(field);
        var response = new CortexRouter(s, new Router.FakeAnalyzer()).Route("nonexistent", new JObject { ["_cortexExpected"] = expected });
        Assert.Equal(RevitCortex.Core.Results.CortexErrorCode.Cancelled, response.Error!.Code);
    }

    [Fact]
    public void StartupRunsOnceAndManualStopSuppressesIt()
    {
        var state = new StartupAttempt();
        Assert.True(state.Take()); Assert.False(state.Take());
        state.Suppress(); Assert.False(state.Take());
        var stoppedEarly = new StartupAttempt(); stoppedEarly.Suppress(); Assert.False(stoppedEarly.Take());
    }

    [Fact]
    public void ContextChangeDuringConfirmationRejectsApproval()
    {
        var request = new ToolRequestLifetime { ContextIsValid = () => false };
        Assert.True(request.TryStart()); request.BeginConfirmation();
        Assert.Throws<DocumentContextChangedException>(() => request.FinishConfirmation());
    }
}
