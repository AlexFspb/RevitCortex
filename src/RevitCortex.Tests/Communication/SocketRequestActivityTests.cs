using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Communication;
using Xunit;
namespace RevitCortex.Tests.Communication;
public class SocketRequestActivityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllRequestsNotifyAndObserverFailuresDoNotBreakResponses(bool observerThrows)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var seen = new ConcurrentQueue<string>();
        var router = new CortexRouter(new CortexSession(new SessionStore()), new Router.FakeAnalyzer());
        var service = new SocketService(router, port, requestReceived: name =>
        {
            seen.Enqueue(name);
            if (observerThrows) throw new InvalidOperationException("indicator failed");
        });
        service.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);
            foreach (var method in new[] { "get_connection_status", "say_hello", "unknown_tool" })
            {
                await writer.WriteLineAsync(new JObject { ["id"] = 1, ["method"] = method }.ToString(Newtonsoft.Json.Formatting.None));
                var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.NotNull(response);
                Assert.Equal(1, JObject.Parse(response!)["id"]!.Value<int>());
                Assert.True(seen.TryDequeue(out var received)); Assert.Equal(method, received);
            }
            foreach (var invalid in new[] { "{broken json", "{}" })
            {
                await writer.WriteLineAsync(invalid);
                var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.NotNull(JObject.Parse(response!)["error"]);
                Assert.True(seen.TryDequeue(out var received)); Assert.Equal("invalid_request", received);
            }
        }
        finally { service.Stop(); }
    }
}
