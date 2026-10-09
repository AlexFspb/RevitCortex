using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;
using Xunit;
namespace RevitCortex.Tests.Bridge;
public class ConnectionDiscoveryTests
{
    private sealed class Endpoint : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        public int Port { get; }
        public string Instance = Guid.NewGuid().ToString("N");
        public long Generation = 1;
        public bool HasDocument = true;
        public bool FailPreparation;
        public int Commands;
        public int Preparations;
        public Endpoint() { _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _ = Loop(); }
        public JObject Identity() => new() { ["protocol"]="RevitCortex/1", ["instanceId"]=Instance,
            ["bridgePort"]=Port, ["documentGeneration"]=Generation, ["documentPresent"]=HasDocument,
            ["activeDocumentTitle"]=HasDocument ? "Project" : null, ["activeDocumentPath"]="service.rvt" };
        private async Task Loop()
        {
            try { while (!_stop.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_stop.Token); _ = Serve(client); } }
            catch (OperationCanceledException) { } catch (SocketException) when (_stop.IsCancellationRequested) { }
        }
        private async Task Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            try
            {
                while (await reader.ReadLineAsync(_stop.Token) is string line)
                {
                    var req=JObject.Parse(line); var method=req.Value<string>("method");
                    JObject data;
                    if (method == "get_connection_status" || method == "get_project_info" || method == "say_hello") data=Identity();
                    else if (method == "ensure_service_document")
                    {
                        Preparations++;
                        if (FailPreparation) data=new JObject { ["success"]=false, ["error"]="preparation failed" };
                        else { HasDocument=true; Generation++; data=Identity(); data["serviceDocumentCreated"]=true; }
                    }
                    else
                    {
                        Commands++;
                        data=new JObject { ["expected"]=req["params"]?["_cortexExpected"]?.DeepClone(), ["success"]=true };
                    }
                    await writer.WriteLineAsync(new JObject { ["id"]=req["id"], ["result"]=data }.ToString(Formatting.None));
                }
            }
            catch (OperationCanceledException) { } catch (IOException) { }
        }
        public void Dispose() { _stop.Cancel(); _listener.Stop(); }
    }

    [Fact]
    public async Task DiscoveryListsFourInstancesWithoutChangingBoundPort()
    {
        using var a=new Endpoint(); using var b=new Endpoint(); using var c=new Endpoint(); using var d=new Endpoint();
        var manager=new RevitConnectionManager(a.Port);
        var rows=await manager.DiscoverAsync(new[] {a.Port,b.Port,c.Port,d.Port}, default);
        Assert.Equal(4, rows.Count); Assert.All(rows, r=>Assert.Equal("reachable",r.Value<string>("status")));
        Assert.Single(rows, r=>r.Value<bool>("isThisConnection"));
        Assert.Equal(a.Port, manager.Port); Assert.Equal(0,a.Commands+b.Commands+c.Commands+d.Commands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedDocumentOrRestartBlocksUntilExplicitInspection(bool restart)
    {
        using var endpoint=new Endpoint(); var manager=new RevitConnectionManager(endpoint.Port);
        await manager.ExecuteAsync("get_project_info",new JObject());
        if(restart) endpoint.Instance=Guid.NewGuid().ToString("N"); else endpoint.Generation++;
        await manager.DiscoverAsync(new[] {endpoint.Port},default);
        var blocked=await manager.ExecuteAsync("modify_element",new JObject());
        Assert.False(blocked.Value<bool>("success")); Assert.Equal(0,endpoint.Commands);
        await manager.ExecuteAsync("get_project_info",new JObject());
        var sent=await manager.ExecuteAsync("modify_element",new JObject());
        Assert.Equal(1,endpoint.Commands);
        Assert.Equal(endpoint.Generation,sent["expected"]!.Value<long>("documentGeneration"));
    }

    [Fact]
    public async Task EmptyRevitPreparesServiceBeforeOneScriptAndCarriesNewIdentity()
    {
        using var endpoint=new Endpoint { HasDocument=false }; var manager=new RevitConnectionManager(endpoint.Port);
        var result=await manager.ExecuteAsync("send_code_to_revit",new JObject { ["code"]="return 1;" });
        Assert.Equal(1,endpoint.Preparations); Assert.Equal(1,endpoint.Commands);
        Assert.Equal(endpoint.Generation,result["expected"]!.Value<long>("documentGeneration"));
        Assert.True(result.Value<bool>("serviceProjectPrepared"));
    }

    [Fact]
    public async Task FailedPreparationNeverSendsOrRetriesScript()
    {
        using var endpoint=new Endpoint { HasDocument=false,FailPreparation=true }; var manager=new RevitConnectionManager(endpoint.Port);
        var result=await manager.ExecuteAsync("send_code_to_revit",new JObject { ["code"]="return 1;" });
        Assert.False(result.Value<bool>("success")); Assert.Equal(1,endpoint.Preparations); Assert.Equal(0,endpoint.Commands);
    }

    [Fact]
    public async Task QueriesDoNotCreateServiceProject()
    {
        using var endpoint=new Endpoint { HasDocument=false }; var manager=new RevitConnectionManager(endpoint.Port);
        await manager.ExecuteAsync("get_project_info",new JObject());
        await manager.ExecuteAsync("get_connection_status",new JObject());
        Assert.Equal(0,endpoint.Preparations);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task DiscoveryRejectsInvalidPorts(int port) =>
        await Assert.ThrowsAsync<ArgumentException>(()=>new RevitConnectionManager().DiscoverAsync(new[]{port},default));
}
