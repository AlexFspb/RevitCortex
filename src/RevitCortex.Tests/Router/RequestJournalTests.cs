using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using Xunit;

namespace RevitCortex.Tests.Router;

public class RequestJournalTests
{
    private sealed class InspectingTool : ICortexTool
    {
        public string Name => "inspect_journal";
        public string Category => "Test";
        public string Description => "Test";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public Action? Inspect;
        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            Inspect!();
            throw new ConfirmationFailedException(new InvalidOperationException("show failed"));
        }
    }

    [Fact]
    public void StartIsDurableBeforeToolAndResponsePreservesIdentityAndTechnicalFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-request-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new AuditLogger(Path.Combine(directory, "audit.jsonl"));
            var session = new CortexSession(new SessionStore()) { BridgePort = 8888 };
            session.UpdateDocumentTitle("Project A");
            var router = new CortexRouter(session, new FakeAnalyzer(), logger);
            var tool = new InspectingTool();
            tool.Inspect = () =>
            {
                var started = JObject.Parse(Assert.Single(File.ReadAllLines(logger.RequestLogPath)));
                Assert.Equal("request_started", started["phase"]);
                Assert.Equal("Project A", started["activeDocumentTitle"]);
                session.UpdateDocumentTitle("Project B"); // Completion must retain the captured target.
            };
            router.RegisterTool(tool);
            var result = router.Route(tool.Name, new JObject());
            Assert.Equal(CortexErrorCode.ConfirmationFailed, result.Error!.Code);
            var lines = File.ReadAllLines(logger.RequestLogPath).Select(JObject.Parse).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.Equal(lines[0]["requestId"], lines[1]["requestId"]);
            Assert.Equal("response_returned", lines[1]["phase"]);
            Assert.Equal("ConfirmationFailed", lines[1]["error_code"]);
            Assert.All(lines, line =>
            {
                Assert.Equal("Project A", line["activeDocumentTitle"]);
                Assert.Equal(8888, line["bridgePort"]);
                Assert.Equal(Environment.ProcessId, line["revitProcessId"]);
                Assert.Equal(CortexBuild.Id, line["buildId"]);
                Assert.Equal(CortexBuild.CoreModuleId, line["coreModuleId"]);
            });
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void InvalidToolStillGetsPairedRequestRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-request-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new AuditLogger(Path.Combine(directory, "audit.jsonl"));
            var router = new CortexRouter(new CortexSession(new SessionStore()), new FakeAnalyzer(), logger);
            router.Route("missing", new JObject());
            var lines = File.ReadAllLines(logger.RequestLogPath).Select(JObject.Parse).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.Equal("InvalidInput", lines[1]["error_code"]);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void TimeoutRecordDoesNotClaimExecutionStopped()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-request-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new AuditLogger(Path.Combine(directory, "audit.jsonl"));
            logger.LogRequest("id", "response_returned", "tool", 8080, null, 1, "", response:
                CortexResult<object>.Fail(CortexErrorCode.Timeout, "timeout"));
            Assert.True(JObject.Parse(File.ReadAllText(logger.RequestLogPath))["executionMayStillBeRunning"]!.Value<bool>());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void UnwritableJournalDoesNotChangeToolOutcome()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cortex-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var logger = new AuditLogger(Path.Combine(directory, "audit.jsonl"));
            Directory.CreateDirectory(logger.RequestLogPath); // File path occupied by a directory.
            logger.LogRequest("id", "request_started", "tool", 8080, null, 1, "");
        }
        finally { Directory.Delete(directory, true); }
    }
}
