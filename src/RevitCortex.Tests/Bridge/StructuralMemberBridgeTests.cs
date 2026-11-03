using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Bridge;

public class StructuralMemberBridgeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[{}] []")]
    [InlineData("[{\"role\":\"beam\",\"role\":\"column\"}]")]
    public async Task InvalidJsonIsRejectedBeforeAnyConnection(string json)
    {
        // Port zero could not be contacted. The wrapper must validate before the bridge.
        var result = JObject.Parse(await StructuralMemberTools.AuthorStructuralMembers(new RevitConnectionManager(0), json));
        Assert.False(result.Value<bool>("success"));
        Assert.Equal("InvalidInput", result["error"]!.Value<string>("code"));
        Assert.False(result["error"]!["context"]!.Value<bool>("commandSent"));
    }

    [Fact]
    public async Task OversizedOrTooDeepJsonIsRejectedLocally()
    {
        var tooLarge = "[\"" + new string('x', 1024 * 1024) + "\"]";
        var tooDeep = new string('[', 20) + "0" + new string(']', 20);
        foreach (var json in new[] { tooLarge, tooDeep, new JArray(Enumerable.Range(0, 101).Select(_ => new JObject())).ToString() })
        {
            var result = JObject.Parse(await StructuralMemberTools.AuthorStructuralMembers(new RevitConnectionManager(0), json));
            Assert.False(result.Value<bool>("success"));
            Assert.False(result["error"]!["context"]!.Value<bool>("commandSent"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StructuralWrappersPreserveFixedPortIdentityAndParameters(bool read)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var respond = Respond(listener, port, read, timeout.Token);
        var manager = new RevitConnectionManager(port);
        var response = read
            ? await StructuralMemberTools.GetLocationCurves(manager, new[] { "StructuralFraming" }, new long[] { 1, 2 }, true, 3, 4, timeout.Token)
            : await StructuralMemberTools.AuthorStructuralMembers(manager, "[{\"sourceId\":\"B1\"}]", ct: timeout.Token);
        Assert.True(JObject.Parse(response).Value<bool>("success"));
        Assert.Equal(port, manager.Port);
        await respond;
    }

    private static async Task Respond(TcpListener listener, int port, bool read, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var inspection = JObject.Parse((await reader.ReadLineAsync(ct))!);
        Assert.Equal("get_connection_status", inspection.Value<string>("method"));
        await Reply(inspection, new JObject { ["protocol"] = "RevitCortex/1", ["bridgePort"] = port,
            ["instanceId"] = "structural-test", ["documentGeneration"] = 5, ["documentPresent"] = true });
        var request = JObject.Parse((await reader.ReadLineAsync(ct))!);
        Assert.Equal(read ? "get_location_curves" : "author_structural_members", request.Value<string>("method"));
        var args = (JObject)request["params"]!;
        Assert.Equal(port, args["_cortexExpected"]!.Value<int>("bridgePort"));
        Assert.Equal(5, args["_cortexExpected"]!.Value<int>("documentGeneration"));
        if (read)
        {
            Assert.Equal(new[] { "StructuralFraming" }, args["categories"]!.ToObject<string[]>());
            Assert.Equal(new long[] { 1, 2 }, args["elementIds"]!.ToObject<long[]>());
            Assert.True(args.Value<bool>("includeTypeParams"));
            Assert.Equal(3, args.Value<int>("offset"));
            Assert.Equal(4, args.Value<int>("limit"));
        }
        else
        {
            Assert.True(args.Value<bool>("dryRun"));
            Assert.False(args.Value<bool>("pin"));
            Assert.Equal("B1", args["members"]![0]!.Value<string>("sourceId"));
        }
        await Reply(request, new JObject { ["success"] = true });

        Task Reply(JObject req, JObject data) => writer.WriteLineAsync(new JObject
            { ["jsonrpc"] = "2.0", ["id"] = req["id"]!.DeepClone(), ["result"] = data }.ToString(Formatting.None));
    }
}
