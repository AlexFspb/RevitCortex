using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>Transaction-scoped warning capture and error rollback for autonomous scripts. No UI or automatic repair.</summary>
public static class ScriptFailureHandling
{
    /// <summary>Call after Start and before model changes on EVERY script-owned transaction.</summary>
    public static FailureCapture Configure(Transaction transaction)
    {
        var capture = new FailureCapture();
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(capture);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
        return capture;
    }

    public sealed class FailureCapture : IFailuresPreprocessor
    {
        // Only primitive data survives beyond PreprocessFailures, never FailureMessageAccessor or ElementId.
        private readonly ScriptFailureReport _report = new();
        public IReadOnlyList<Dictionary<string, object>> Failures => _report.Failures;
        public int OmittedFailures => _report.OmittedFailures;
        public string? DiagnosticReportPath { get; private set; }

        public void AppendWarnings(Newtonsoft.Json.Linq.JObject response) =>
            _report.AppendWarnings(response, DiagnosticReportPath);

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (var failure in accessor.GetFailureMessages())
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.None) continue;
                bool warning = _report.Record(severity.ToString(), failure.GetDescriptionText() ?? "",
                    failure.GetFailingElementIds().Select(id => id.Value));
                // Always process severity, even when diagnostic budgets have been exhausted.
                if (warning) accessor.DeleteWarning(failure);
            }
            if (_report.WarningCount > 0 || _report.HasErrors) SaveReport();
            return _report.HasErrors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }

        public CortexResult<object> ToFailure(TransactionStatus status)
        {
            var rolledBack = status == TransactionStatus.RolledBack;
            return CortexResult<object>.Fail(CortexErrorCode.TransactionFailed,
                rolledBack ? "Revit rejected the script transaction; changes in this transaction were rolled back."
                    : "Revit did not commit the script transaction; rollback is not confirmed.",
                suggestion: "Inspect failure descriptions and element IDs. Verify model state before retrying. Use a bounded dry-run before bulk replacement; do not force-accept failures or delete affected elements as recovery.",
                context: new Dictionary<string, object>
                {
                    ["transactionState"] = rolledBack ? "rolled_back" : status.ToString(),
                    ["failures"] = Failures, ["omittedFailures"] = OmittedFailures,
                    ["warningCount"] = _report.WarningCount,
                    ["diagnosticReportPath"] = DiagnosticReportPath ?? "(report could not be saved)",
                    ["externalEffectsMayRemain"] = true
                });
        }

        private void SaveReport()
        {
            try
            {
                var folder = Path.Combine(CortexEnvironment.Current.SupportReportsFolder, "script-failures");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(new
                {
                    utc = DateTime.UtcNow, revitProcessId = Environment.ProcessId,
                    action = _report.HasErrors ? "rollback_requested" : "warnings_removed",
                    failures = Failures, omittedFailures = OmittedFailures, warningCount = _report.WarningCount
                }, Formatting.Indented));
                DiagnosticReportPath = path;
            }
            catch { /* Diagnostics must not prevent rollback; absence is explicit in ToFailure. */ }
        }
    }
}
