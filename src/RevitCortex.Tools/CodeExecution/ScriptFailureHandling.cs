using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>Strict, transaction-scoped handling for autonomous scripts. No UI or automatic repair.</summary>
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
        private readonly List<Dictionary<string, object>> _failures = new();
        public IReadOnlyList<Dictionary<string, object>> Failures => _failures;
        public int OmittedFailures { get; private set; }
        public string? DiagnosticReportPath { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            var unexpected = false;
            foreach (var failure in accessor.GetFailureMessages())
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.None) continue;
                unexpected = true;
                if (_failures.Count >= 100) { OmittedFailures++; continue; }
                var description = failure.GetDescriptionText() ?? "";
                var ids = failure.GetFailingElementIds();
                _failures.Add(new Dictionary<string, object>
                {
                    ["description"] = description.Length > 2048 ? description.Substring(0, 2048) : description,
                    ["descriptionTruncated"] = description.Length > 2048,
                    ["severity"] = severity.ToString(),
                    ["elementIds"] = ids.Take(200).Select(id => id.Value).ToArray(),
                    ["elementIdsTruncated"] = ids.Count > 200
                });
            }
            if (!unexpected) return FailureProcessingResult.Continue;
            SaveReport();
            return FailureProcessingResult.ProceedWithRollBack;
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
                    ["failures"] = _failures, ["omittedFailures"] = OmittedFailures,
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
                    action = "rollback_requested", failures = _failures, omittedFailures = OmittedFailures
                }, Formatting.Indented));
                DiagnosticReportPath = path;
            }
            catch { /* Diagnostics must not prevent rollback; absence is explicit in ToFailure. */ }
        }
    }
}
