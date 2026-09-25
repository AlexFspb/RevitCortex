using Xunit;

namespace RevitCortex.Tests.Tools;

// Native Revit transactions cannot be constructed by the test runner. These wiring guards
// supplement projector behavior tests; real rollback/dialog behavior still requires Revit.
public class ScriptSafetySourceTests
{
    private static string Source(params string[] parts) => File.ReadAllText(Path.GetFullPath(
        Path.Combine(new[] { "..", "..", "..", "..", "RevitCortex.Tools" }.Concat(parts).ToArray())));

    [Fact]
    public void ResponsePreparationPrecedesBothCommitBoundaries()
    {
        var source = Source("CodeExecution", "RoslynExecutor.cs");
        var group = source.IndexOf("using var txGroup", StringComparison.Ordinal);
        var auto = source.IndexOf("using var tx =", StringComparison.Ordinal);
        Assert.True(source.IndexOf("prepared = Prepare();", group, StringComparison.Ordinal) < source.IndexOf("txGroup.Assimilate()", StringComparison.Ordinal));
        Assert.True(source.IndexOf("prepared = Prepare();", auto, StringComparison.Ordinal) < source.IndexOf("tx.Commit()", StringComparison.Ordinal));
        Assert.Contains("TryRollback(txGroup.GetStatus, txGroup.RollBack)", source);
        Assert.Contains("TryRollback(tx.GetStatus, tx.RollBack)", source);
        Assert.DoesNotContain("ReferenceLoopHandling", source);
        Assert.DoesNotContain("FromObject(result.Data)", Source("Elements", "SendCodeToRevitTool.cs"));
    }

    [Fact]
    public void WarningHandlerIsInstalledBeforeScriptAndNeverDeletesModelElements()
    {
        var source = Source("CodeExecution", "RoslynExecutor.cs");
        var start = source.IndexOf("tx.Start();", StringComparison.Ordinal);
        var configure = source.IndexOf("ScriptFailureHandling.Configure(tx)", StringComparison.Ordinal);
        var invoke = source.IndexOf("prepared = Prepare();", start, StringComparison.Ordinal);
        Assert.True(start < configure && configure < invoke);
        Assert.Contains("txFailures.ToFailure(status)", source);
        var helper = Source("CodeExecution", "ScriptFailureHandling.cs");
        Assert.Contains("IFailuresPreprocessor", helper);
        Assert.Contains("SetClearAfterRollback(true)", helper);
        Assert.Contains("FailureProcessingResult.ProceedWithRollBack", helper);
        Assert.Contains("GetDescriptionText()", helper);
        Assert.Contains("GetFailingElementIds()", helper);
        Assert.Contains("id.Value", helper);
        Assert.Contains("if (warning) accessor.DeleteWarning(failure)", helper);
        Assert.Contains("_report.HasErrors ? FailureProcessingResult.ProceedWithRollBack", helper);
        Assert.Contains("txFailures.AppendWarnings(prepared)", source);
        Assert.Contains("value is ElementId id ? id.Value : null", source);
        Assert.Contains("ScriptFailureReport.ReserveBytes", source);
        Assert.DoesNotContain("ResolveFailure(", helper);
        Assert.DoesNotContain("DeleteElements(", helper);
    }
}
