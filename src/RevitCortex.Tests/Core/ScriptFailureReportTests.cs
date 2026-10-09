using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using Xunit;

namespace RevitCortex.Tests.Core;

public class ScriptFailureReportTests
{
    [Theory]
    [InlineData("None", false, false)]
    [InlineData("None", true, false)]
    [InlineData("Warning", false, false)]
    [InlineData("Warning", true, true)]
    [InlineData("Error", false, true)]
    [InlineData("Error", true, true)]
    public void StrictWarningPolicyDoesNotChangeCompatibleDefault(string severity, bool strict, bool expected)
    {
        var report = new ScriptFailureReport();
        report.Record(severity, "Test failure", new long[] { 123 });
        Assert.Equal(expected, report.RequiresRollback(strict));
    }

    [Theory]
    [InlineData(true, false, "RolledBack", true)]
    [InlineData(true, true, "RolledBack", false)]
    [InlineData(false, false, "RolledBack", false)]
    [InlineData(true, false, "Pending", false)]
    public void FailureResponseDistinguishesStrictPolicyErrorsAndUnconfirmedRollback(
        bool strict, bool error, string status, bool strictMessage)
    {
        var report = new ScriptFailureReport();
        report.Record("Warning", "Duplicate mark", new long[] { 12 });
        if (error) report.Record("Error", "Cannot rotate", new long[] { 13 });
        var result = report.ToFailure(status, strict, "diagnostic.json");
        Assert.Equal(CortexErrorCode.TransactionFailed, result.Error!.Code);
        Assert.Equal(strictMessage, result.Error.Message.Contains("strict warning policy (no errors)"));
        Assert.Equal(strict, result.Error.Context!["rollbackOnWarnings"]);
        Assert.Equal(error, result.Error.Context["hasErrors"]);
        Assert.Equal(status == "RolledBack" ? "rolled_back" : status, result.Error.Context["transactionState"]);
        Assert.Equal("diagnostic.json", result.Error.Context["diagnosticReportPath"]);
        if (status == "Pending") Assert.Contains("rollback is not confirmed", result.Error.Message);
    }

    [Fact]
    public void StrictPolicyStillRollsBackAfterDiagnosticBudgetIsExhausted()
    {
        var report = new ScriptFailureReport();
        for (int i = 0; i < 110; i++) report.Record("Warning", "Warning", new long[] { i });
        Assert.True(report.OmittedFailures > 0);
        Assert.True(report.RequiresRollback(true));
        Assert.False(report.RequiresRollback(false));
    }

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
