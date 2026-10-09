using System;
using System.IO;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Session;

namespace RevitCortex.Plugin.UI;

public static class ConfirmationHelper
{
    public static bool? Confirm(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true;
        OperationConfirmationWindow? dialog = null;
        try
        {
            dialog = new OperationConfirmationWindow(action, elementCount, description);
            return ConfirmationPresentation.Show(dialog, dialog.ShowConfirmation);
        }
        catch (ConfirmationExpiredException) { throw; }
        catch (Exception ex) { throw ReportFailure(ex); }
        finally { dialog?.CleanupConfirmation(); }
    }

    public static bool? ConfirmCritical(string action, int elementCount, string? description)
    {
        if (elementCount <= 0) return true;
        CriticalConfirmationWindow? dialog = null;
        try
        {
            dialog = new CriticalConfirmationWindow(action, elementCount, description);
            return ConfirmationPresentation.Show(dialog, dialog.ShowConfirmation);
        }
        catch (ConfirmationExpiredException) { throw; }
        catch (Exception ex) { throw ReportFailure(ex); }
        finally { dialog?.CleanupConfirmation(); }
    }

    private static ConfirmationFailedException ReportFailure(Exception exception)
    {
        var failure = exception as ConfirmationFailedException ?? new ConfirmationFailedException(exception);
        System.Diagnostics.Trace.WriteLine($"[RevitCortex] Confirmation failed: {exception}");
        try
        {
            var directory = Path.Combine(CortexEnvironment.Current.SupportReportsFolder, "confirmation");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path,
                $"UTC: {DateTime.UtcNow:o}\nPID: {Environment.ProcessId}\nBuild: {CortexBuild.Id}\n{exception}");
            failure.DiagnosticReportPath = path;
        }
        catch { /* Logging must never turn a failed confirmation into approval. */ }
        return failure;
    }

    public static Core.Results.CortexResult<object> CancelledResult() => Core.Results.CortexResult<object>.Fail(
        Core.Results.CortexErrorCode.Cancelled, "Operation was not approved.",
        suggestion: "Verify model and document context before continuing; do not retry automatically.");
}
