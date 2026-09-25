using System;
using System.Collections.Generic;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Session;

/// <summary>One modal presentation, including failed presentation and late timer callbacks.</summary>
public sealed class ConfirmationDialogLifecycle
{
    private bool _used;
    private bool _showing;
    private bool _loaded;
    private bool _finished;
    private Exception? _failure;
    public bool IsActive => _showing && _loaded && !_finished;

    public bool MarkLoaded()
    {
        if (!_showing || _finished) return false;
        _loaded = true;
        return true;
    }

    public bool? Show(Func<bool?> showDialog, Action cleanup)
    {
        if (_used) throw new ConfirmationFailedException(new InvalidOperationException("Confirmation cannot be shown twice."));
        _used = _showing = true;
        try
        {
            var result = showDialog();
            if (_failure != null) throw new ConfirmationFailedException(_failure);
            return result == true;
        }
        catch (ConfirmationFailedException) { throw; }
        catch (Exception ex) { throw new ConfirmationFailedException(ex); }
        finally
        {
            End();
            cleanup();
        }
    }

    public void End() { _finished = true; _showing = false; }

    // Never throw from a DispatcherTimer callback. The owning Show reports the failure.
    public void Complete(bool accepted, Action<bool> setDialogResult, Action closeAfterFailure)
    {
        if (!IsActive) return;
        _finished = true;
        try { setDialogResult(accepted); }
        catch (Exception ex)
        {
            _failure = ex;
            try { closeAfterFailure(); }
            catch (Exception closeError) { _failure = new AggregateException(ex, closeError); }
        }
    }
}

public sealed class ConfirmationFailedException : Exception
{
    public string? DiagnosticReportPath { get; set; }

    public ConfirmationFailedException(Exception inner)
        : base("The confirmation dialog could not be displayed or completed. This is a technical failure, not a user cancellation.", inner) { }

    public CortexResult<object> ToResult() => CortexResult<object>.Fail(
        CortexErrorCode.ConfirmationFailed, Message,
        suggestion: "The operation was not approved. Verify model/context before issuing a new request; do not retry automatically. See the local confirmation diagnostic report.",
        context: new Dictionary<string, object>
        {
            ["operationApproved"] = false,
            ["diagnosticReportPath"] = DiagnosticReportPath ?? "(report unavailable)"
        });
}
