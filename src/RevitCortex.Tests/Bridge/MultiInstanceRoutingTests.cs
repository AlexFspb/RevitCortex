using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Server.Connection;
using Xunit;

namespace RevitCortex.Tests.Bridge;

public class MultiInstanceRoutingTests
{
    [Fact]
    public async Task TwoManagers_ReachOnlyTheirAssignedListener()
    {
        using var first = new TcpListener(IPAddress.Loopback, 0);
        using var second = new TcpListener(IPAddress.Loopback, 0);
        first.Start();
        second.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var firstPort = ((IPEndPoint)first.LocalEndpoint).Port;
        var secondPort = ((IPEndPoint)second.LocalEndpoint).Port;
        var firstManager = new RevitConnectionManager(CortexPort.Resolve(firstPort.ToString()));
        var secondManager = new RevitConnectionManager(CortexPort.Resolve(secondPort.ToString()));
        var firstResponse = Respond(first, "Revit A", timeout.Token);
        var secondResponse = Respond(second, "Revit B", timeout.Token);

        var results = await Task.WhenAll(
            firstManager.ExecuteAsync("get_project_info", new JObject(), timeout.Token),
            secondManager.ExecuteAsync("get_project_info", new JObject(), timeout.Token));

        Assert.Equal("Revit A", results[0]["document"]?.Value<string>());
        Assert.Equal("Revit B", results[1]["document"]?.Value<string>());
        await Task.WhenAll(firstResponse, secondResponse);

        // Closing A must not send A's next command to B, even while B is available.
        first.Stop();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            firstManager.ExecuteAsync("get_project_info", new JObject(), timeout.Token));
        var remainingResponse = Respond(second, "Revit B", timeout.Token);
        var remaining = await secondManager.ExecuteAsync("get_project_info", new JObject(), timeout.Token);
        Assert.Equal("Revit B", remaining["document"]?.Value<string>());
        await remainingResponse;
    }

    private static async Task Respond(TcpListener listener, string document, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var request = JObject.Parse((await reader.ReadLineAsync(ct))!);
        Assert.Equal("get_project_info", request["method"]?.Value<string>());
        var response = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = request["id"],
            ["result"] = new JObject { ["document"] = document }
        };
        await writer.WriteLineAsync(response.ToString(Formatting.None));
    }
}
