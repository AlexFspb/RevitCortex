using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Bridge;

public class MepRelationshipBridgeTests
{
    [Theory]
    [InlineData("create_pipe", false)]
    [InlineData("create_pipe", true)]
    [InlineData("create_duct", false)]
    [InlineData("create_duct", true)]
    [InlineData("get_element_relationships", false)]
    [InlineData("get_element_relationships", true)]
    public async Task WrappersForwardUnitsFlagsAndBoundContextToOnlyTheirPort(string method, bool explicitOptions)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serve = Respond(listener, port, method, deadline.Token);
        var manager = new RevitConnectionManager(port);
        string response;
        if (method == "create_pipe")
            response = explicitOptions
                ? await MepCreationTools.CreatePipe(manager, 11, 12, 13, new[] { 100d, 200d, 300d }, new[] { 1100d, 200d, 300d }, 25, false, deadline.Token)
                : await MepCreationTools.CreatePipe(manager, 11, 12, 13, new[] { 100d, 200d, 300d }, new[] { 1100d, 200d, 300d }, 25, ct: deadline.Token);
        else if (method == "create_duct")
            response = explicitOptions
                ? await MepCreationTools.CreateDuct(manager, 11, 12, 13, new[] { 100d, 200d, 300d }, new[] { 1100d, 200d, 300d }, "rectangular", widthMm: 400, heightMm: 200, dryRun: false, ct: deadline.Token)
                : await MepCreationTools.CreateDuct(manager, 11, 12, 13, new[] { 100d, 200d, 300d }, new[] { 1100d, 200d, 300d }, "round", diameterMm: 100, ct: deadline.Token);
        else
            response = explicitOptions
                ? await ElementRelationshipTools.GetElementRelationships(manager, new long[] { 20, 30 }, false, 2, false, false, true, false, 4, deadline.Token)
                : await ElementRelationshipTools.GetElementRelationships(manager, new long[] { 20, 30 }, ct: deadline.Token);

        var result = JObject.Parse(response);
        Assert.True(result.Value<bool>("success"));
        Assert.Equal(port, manager.Port);
        var input = (JObject)result["received"]!;
        Assert.Equal(port, input["_cortexExpected"]!.Value<int>("bridgePort"));
        Assert.Equal("mep-test", input["_cortexExpected"]!.Value<string>("instanceId"));
        Assert.Equal(7, input["_cortexExpected"]!.Value<int>("documentGeneration"));
        if (method == "get_element_relationships")
        {
            Assert.Equal(new long[] { 20, 30 }, input["elementIds"]!.ToObject<long[]>());
            foreach (var name in new[] { "includeHostChain", "includeSubComponents", "includeInserts", "includeAdaptivePoints" })
                Assert.Equal(!explicitOptions, input.Value<bool>(name));
            Assert.Equal(explicitOptions, input.Value<bool>("includeDependents"));
            Assert.Equal(explicitOptions ? 2 : 8, input.Value<int>("maxDepth"));
            Assert.Equal(explicitOptions ? 4 : 50, input.Value<int>("maxRelated"));
        }
        else
        {
            Assert.Equal(!explicitOptions, input.Value<bool>("dryRun"));
            Assert.Equal(11, input.Value<long>("systemTypeId"));
            Assert.Equal(12, input.Value<long>(method == "create_pipe" ? "pipeTypeId" : "ductTypeId"));
            Assert.Equal(13, input.Value<long>("levelId"));
            Assert.Equal(new[] { 100d, 200d, 300d }, input["startPoint"]!.ToObject<double[]>());
            Assert.Equal(1100d, input["endPoint"]![0]!.Value<double>());
            if (method == "create_pipe") Assert.Equal(25d, input.Value<double>("diameterMm"));
            else if (explicitOptions)
            {
                Assert.Equal("rectangular", input.Value<string>("shape"));
                Assert.Equal(400d, input.Value<double>("widthMm"));
                Assert.Equal(200d, input.Value<double>("heightMm"));
                Assert.Null(input["diameterMm"]);
            }
            else
            {
                Assert.Equal("round", input.Value<string>("shape"));
                Assert.Equal(100d, input.Value<double>("diameterMm"));
                Assert.Null(input["widthMm"]);
                Assert.Null(input["heightMm"]);
            }
        }
        await serve;
    }

    private static async Task Respond(TcpListener listener, int port, string method, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        var inspection = JObject.Parse((await reader.ReadLineAsync(ct))!);
        Assert.Equal("get_connection_status", inspection.Value<string>("method"));
        await Reply(inspection, new JObject { ["protocol"] = "RevitCortex/1", ["bridgePort"] = port,
            ["instanceId"] = "mep-test", ["documentGeneration"] = 7, ["documentPresent"] = true });
        var request = JObject.Parse((await reader.ReadLineAsync(ct))!);
        Assert.Equal(method, request.Value<string>("method"));
        await Reply(request, new JObject { ["success"] = true, ["received"] = request["params"]!.DeepClone() });

        Task Reply(JObject req, JObject data) => writer.WriteLineAsync(new JObject
            { ["jsonrpc"] = "2.0", ["id"] = req["id"]!.DeepClone(), ["result"] = data }.ToString(Formatting.None));
    }
}
