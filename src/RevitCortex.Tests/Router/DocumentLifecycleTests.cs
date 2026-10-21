using System.Net;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Communication;
using Xunit;

namespace RevitCortex.Tests.Router;

public class DocumentLifecycleTests
{
    private sealed class DocumentWrapper(int id)
    {
        public int Id { get; } = id;
        public int Comparisons { get; private set; }
        public override bool Equals(object? other)
        {
            Comparisons++;
            return other is DocumentWrapper wrapper && Id == wrapper.Id;
        }
        public override int GetHashCode() => Id;
    }

    [Fact]
    public async Task EquivalentWrappers_PreserveContext_WorkerValidationNeverCallsDocumentEquals()
    {
        var (session, router) = Create();
        var original = new DocumentWrapper(1);
        router.SynchronizeActiveDocument(original);
        var pending = session.CaptureDocumentContext();
        router.SynchronizeActiveDocument(new DocumentWrapper(1));
        Assert.True(session.IsCurrentDocumentContext(pending));
        Assert.True(original.Comparisons > 0);
        var comparisons = original.Comparisons;
        Assert.True(await Task.Run(() => session.IsCurrentDocumentContext(pending)));
        Assert.Equal(comparisons, original.Comparisons);
        router.OnDocumentClosing(new DocumentWrapper(2));
        Assert.True(session.IsCurrentDocumentContext(pending));
        router.OnDocumentClosing(new DocumentWrapper(1));
        Assert.Null(session.CaptureDocumentContext().Document);
        Assert.False(session.IsCurrentDocumentContext(pending));
    }

    private static (CortexSession Session, CortexRouter Router) Create()
    {
        var session = new CortexSession(new SessionStore());
        return (session, new CortexRouter(session, new FakeAnalyzer()));
    }

    [Fact]
    public void ClosingBackgroundFamily_PreservesProjectContextAndPendingCommands()
    {
        var (session, router) = Create();
        var project = new object();
        router.SynchronizeActiveDocument(project);
        var context = session.CaptureDocumentContext();
        session.Store.Set("projectData", new object());
        router.OnDocumentClosing(new object());
        Assert.True(session.IsCurrentDocumentContext(context));
        Assert.Same(project, session.Store.Get<object>("activeDocument"));
        Assert.True(session.Store.ContainsKey("projectData"));
    }

    [Fact]
    public void ClosingActiveDocument_InvalidatesPendingCommands_AndCancelledCloseRestoresContext()
    {
        var (session, router) = Create();
        var project = new object();
        router.SynchronizeActiveDocument(project);
        var pending = session.CaptureDocumentContext();
        router.OnDocumentClosing(project);
        Assert.Null(session.CaptureDocumentContext().Document);
        Assert.False(session.IsCurrentDocumentContext(pending));
        router.SynchronizeActiveDocument(project); // Revit cancelled the close.
        Assert.Same(project, session.CaptureDocumentContext().Document);
        Assert.False(session.IsCurrentDocumentContext(pending));
    }

    [Fact]
    public void SwitchingAwayAndBack_DoesNotReviveOldCommands()
    {
        var (session, router) = Create();
        var project = new object();
        router.SynchronizeActiveDocument(project);
        var pending = session.CaptureDocumentContext();
        router.SynchronizeActiveDocument(new object());
        router.SynchronizeActiveDocument(project);
        Assert.False(session.IsCurrentDocumentContext(pending));
    }

    [Fact]
    public void IdlingInSameDocumentAndOrdinaryEdits_DoNotInvalidatePendingCommands()
    {
        var (session, router) = Create();
        var project = new object();
        router.SynchronizeActiveDocument(project);
        var pending = session.CaptureDocumentContext();
        router.SynchronizeActiveDocument(project);
        session.BumpDocumentVersion();
        Assert.True(session.IsCurrentDocumentContext(pending));
    }

    [Fact]
    public void LateResultFromOldDocument_CannotPopulateNewSessionCache()
    {
        var (session, router) = Create();
        router.SynchronizeActiveDocument(new object());
        var executions = 0;
        var tool = new DocumentTool(() =>
        {
            executions++;
            if (executions == 1) router.SynchronizeActiveDocument(new object());
            return executions;
        });
        router.RegisterTool(tool);
        router.Route(tool.Name, new JObject());
        router.Route(tool.Name, new JObject());
        router.Route(tool.Name, new JObject());
        Assert.Equal(2, executions);
    }

    [Fact]
    public async Task ExistingTcpConnection_SurvivesFamilyAndLastDocumentClose_AndReopen()
    {
        var (session, router) = Create();
        var project = new object();
        router.SynchronizeActiveDocument(project);
        router.RegisterTool(new DocumentTool(() => "project data"));
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var service = new SocketService(router, port);
        service.Start();
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8);
            async Task<JObject> Query()
            {
                await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"get_lifecycle_test\",\"params\":{}}");
                var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.NotNull(response);
                return JObject.Parse(response!);
            }

            Assert.NotNull((await Query())["result"]);
            router.OnDocumentClosing(new object()); // Temporary background family.
            Assert.NotNull((await Query())["result"]);
            router.OnDocumentClosing(project);
            router.SynchronizeActiveDocument(null); // Last project closed.
            var noDocument = await Query();
            Assert.Contains("No document open", noDocument["error"]!.ToString());
            Assert.True(service.IsRunning);
            router.SynchronizeActiveDocument(new object());
            Assert.NotNull((await Query())["result"]);
            Assert.True(service.IsRunning);
        }
        finally { service.Stop(); }
    }

    private sealed class DocumentTool(Func<object> run) : ICortexTool, ICacheableTool
    {
        public string Name => "get_lifecycle_test";
        public string Category => "Test";
        public string Description => "Lifecycle test";
        public bool RequiresDocument => true;
        public bool IsDynamic => false;
        public CacheScope CacheScope => CacheScope.Session;
        public CortexResult<object> Execute(JObject input, CortexSession session) => CortexResult<object>.Ok(run());
    }
}
