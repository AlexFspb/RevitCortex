using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Addins;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Server.Connection;
using RevitCortex.Tests.Router;
using Xunit;

namespace RevitCortex.Tests.Addins;

public class AddinHostTests
{
    [Fact] public async Task InvalidMcpJsonExplainsErrorWithoutSendingCommand()
    {
        var result = JObject.Parse(await RevitCortex.Server.Tools.AddinTools.Execute(new RevitConnectionManager(1),
            Guid.NewGuid().ToString(), "test", "read", "{broken", "{}"));
        Assert.False(result.Value<bool>("success")); Assert.False(result["error"]!["context"]!.Value<bool>("commandSent"));
        Assert.Equal("InvalidJson", (string?)result["error"]?["context"]?["reason"]);
    }
    private sealed class NeverSchedule : IAddinScheduler
    {
        public bool TrySchedule(Func<object?, CortexResult<object>> run, Action<CortexResult<object>> completed) => throw new Exception("Control request must not schedule Revit");
    }
    [Fact] public void RouterControlWorksWithoutDocumentDespiteReadOnlyAndStaleGenericTarget()
    {
        var session = new CortexSession(new SessionStore());
        var router = new CortexRouter(session, new FakeAnalyzer()) { ReadOnlyMode = true };
        router.RegisterTool(new AddinListTool());
        router.Addins = new(new AddinRegistry(), new NeverSchedule(), session.ConnectionStatus, () => router.ReadOnlyMode,
            name => !router.DisabledTools.Contains(name), _ => false, (_, _, _) => throw new Exception("No UI check for list"));
        var result = router.Route("addin_list", new JObject { ["_cortexExpected"] = new JObject { ["instanceId"] = "stale" } });
        Assert.True(result.Success); Assert.IsType<JObject>(result.Data);
        router.SetDisabledTools(new[] { "addin_list" }); Assert.False(router.Route("addin_list", new()).Success);
    }
    [Fact] public void DocumentTokenChangesOnReinitializeAndIsNullWhenEmpty()
    {
        var session = new CortexSession(new SessionStore()); Assert.Null(session.ConnectionStatus().Value<string>("documentToken"));
        session.Reinitialize(new DocumentCapabilities(), "en", new object()); var first = session.ConnectionStatus().Value<string>("documentToken");
        Assert.NotNull(first); Assert.Equal(first, session.ConnectionStatus().Value<string>("documentToken"));
        session.Reinitialize(new DocumentCapabilities(), "en", new object()); Assert.NotEqual(first, session.ConnectionStatus().Value<string>("documentToken"));
        session.Reinitialize(new DocumentCapabilities(), "en"); Assert.Null(session.ConnectionStatus().Value<string>("documentToken"));
    }
    [Fact] public async Task McpControlConnectionBypassesLongRunningRequestMutex()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); var ct = timeout.Token;
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var longStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Respond(StreamWriter writer, JObject request, JToken result) =>
            await writer.WriteLineAsync(new JObject { ["id"] = request["id"], ["jsonrpc"] = "2.0", ["result"] = result }.ToString(Formatting.None));
        var server = Task.Run(async () =>
        {
            using var first = await listener.AcceptTcpClientAsync(ct);
            using var reader = new StreamReader(first.GetStream()); using var writer = new StreamWriter(first.GetStream()) { AutoFlush = true };
            var hello = JObject.Parse((await reader.ReadLineAsync(ct))!);
            await Respond(writer, hello, new JObject { ["protocol"] = "RevitCortex/1", ["instanceId"] = "A", ["documentGeneration"] = 1, ["bridgePort"] = port });
            var slow = JObject.Parse((await reader.ReadLineAsync(ct))!); longStarted.TrySetResult();
            using var second = await listener.AcceptTcpClientAsync(ct);
            using var controlReader = new StreamReader(second.GetStream()); using var controlWriter = new StreamWriter(second.GetStream()) { AutoFlush = true };
            var control = JObject.Parse((await controlReader.ReadLineAsync(ct))!); Assert.Equal("addin_list", (string?)control["method"]);
            await Respond(controlWriter, control, new JObject { ["state"] = "busy", ["port"] = port });
            await finish.Task.WaitAsync(ct); await Respond(writer, slow, new JObject { ["done"] = true });
        }, ct);
        try
        {
            var manager = new RevitConnectionManager(port); var slow = manager.ExecuteAsync("slow_tool", new(), ct);
            await longStarted.Task.WaitAsync(ct);
            var status = await manager.ExecuteAsync("addin_list", new(), ct).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal("busy", (string?)status["state"]); Assert.Equal(port, (int?)status["port"]); Assert.False(slow.IsCompleted);
            finish.TrySetResult(); await slow; await server;
        }
        finally { finish.TrySetResult(); listener.Stop(); }
    }
}
