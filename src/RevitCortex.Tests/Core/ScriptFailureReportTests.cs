using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Core;

public class ScriptFailureReportTests
{
    [Fact]
    public void WarningsAreReturnedWithoutRequestingRollback()
    {
        var report = new ScriptFailureReport();
        Assert.True(report.Record("Warning", "Duplicate mark", new long[] { 12, 3000000000 }));
        Assert.False(report.HasErrors);
        var response = SafeScriptResultProjector.Project(new { count = 2 });
        report.AppendWarnings(response, "report.json");
        Assert.Equal(1, response["warningCount"]!.Value<int>());
        Assert.Equal("Duplicate mark", response["warnings"]![0]!["description"]);
        Assert.Equal(3000000000L, response["warnings"]![0]!["elementIds"]![1]!.Value<long>());
        Assert.Equal(2, response["result"]!["count"]);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("DocumentCorruption")]
    [InlineData("UnknownSeverity")]
    public void ErrorAfterCaptureBudgetStillRequestsRollback(string severity)
    {
        var report = new ScriptFailureReport();
        for (int i = 0; i < 120; i++) Assert.True(report.Record("Warning", "Warning", new long[] { i }));
        Assert.False(report.Record(severity, "Must roll back", new long[] { 5 }));
        Assert.True(report.HasErrors);
        Assert.Equal(120, report.WarningCount);
        Assert.Equal(21, report.OmittedFailures);
    }

    [Fact]
    public void MixedFailuresKeepWarningAndErrorDetails()
    {
        var report = new ScriptFailureReport();
        report.Record("Warning", "Off axis", new long[] { 1 });
        report.Record("Error", "Cannot rotate", new long[] { 2 });
        Assert.True(report.HasErrors);
        Assert.Equal(2, report.Failures.Count);
        Assert.Equal("Cannot rotate", report.Failures[1]["description"]);
    }

    [Fact]
    public void DiagnosticBudgetFitsReservedSpaceIncludingEscapingAndTruncation()
    {
        var report = new ScriptFailureReport();
        for (int i = 0; i < 150; i++)
            report.Record("Warning", new string('я', 3000), Enumerable.Range(0, 300).Select(n => (long)n));
        Assert.True(report.OmittedFailures > 0);
        Assert.Equal(true, report.Failures[0]["descriptionTruncated"]);
        Assert.Equal(true, report.Failures[0]["elementIdsTruncated"]);
        var response = new JObject();
        report.AppendWarnings(response, new string('я', 2000));
        var json = JsonConvert.SerializeObject(response, new JsonSerializerSettings
            { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii });
        Assert.True(json.Length < ScriptFailureReport.ReserveBytes);
    }

    [Fact]
    public void EmptyCaptureDoesNotAddMetadata()
    {
        var report = new ScriptFailureReport();
        report.Record("None", "", Array.Empty<long>());
        var response = new JObject();
        report.AppendWarnings(response, null);
        Assert.Empty(response.Properties());
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void AutoDiagnosticReserveIsEnforcedBeforeCommit()
    {
        var data = Enumerable.Repeat(new string('a', 250000), 4).ToArray();
        SafeScriptResultProjector.Project(data);
        Assert.Equal("ResponseSizeLimit", Assert.Throws<ScriptResultException>(() =>
            SafeScriptResultProjector.Project(data, diagnosticReserveBytes: ScriptFailureReport.ReserveBytes)).Reason);
    }
}
