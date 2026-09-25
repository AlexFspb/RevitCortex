using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Session;

/// <summary>One request, one result. Expiration never frees a still-running ExternalEvent.</summary>
public sealed class ToolRequestLifetime
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _expiration = new();
    private readonly TaskCompletionSource<CortexResult<object>> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _phase = "queued";
    private string? _expiredPhase;
    public CancellationToken Expiration => _expiration.Token;
    public string Phase { get { lock (_gate) return _phase; } }
    public bool IsExpired { get { lock (_gate) return _expiredPhase != null; } }
    public Task<CortexResult<object>> Completion => _completion.Task;
    public IntPtr OwnerHandle { get; set; } // Captured from UIApplication on the Revit UI thread.
    [ThreadStatic] private static ToolRequestLifetime? _current;
    public static ToolRequestLifetime? Current => _current;

    public IDisposable Enter()
    {
        var previous = _current;
        _current = this;
        return new Scope(() => _current = previous);
    }

    public bool TryStart()
    {
        lock (_gate)
        {
            if (_expiredPhase != null || _phase != "queued") return false;
            _phase = "executing";
            return true;
        }
    }

    public void BeginConfirmation()
    {
        lock (_gate)
        {
            ThrowIfExpired();
            _phase = "awaiting_confirmation";
        }
    }

    public void FinishConfirmation()
    {
        // Linearization point: timeout wins => no permission to run after the dialog.
        // Approval wins => a subsequent timeout must report that execution may continue.
        lock (_gate) { ThrowIfExpired(); _phase = "executing"; }
    }

    public void ThrowIfExpired()
    {
        lock (_gate) if (_expiredPhase != null) throw new ConfirmationExpiredException();
    }

    public CortexResult<object> Expire(string toolName, int timeoutMs)
    {
        lock (_gate)
        {
            if (_completion.Task.IsCompleted) return _completion.Task.Result;
            _expiredPhase ??= _phase;
        }
        // Registered callbacks only enqueue UI cleanup, never synchronously wait for the UI.
        try { _expiration.Cancel(); } catch (AggregateException) { }
        return TimeoutResult(toolName, timeoutMs);
    }

    public CortexResult<object> TimeoutResult(string toolName, int timeoutMs)
    {
        lock (_gate)
        {
            var phase = _expiredPhase ?? _phase;
            return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                $"Tool '{toolName}' timed out after {timeoutMs}ms; phase: {phase}.",
                suggestion: phase == "awaiting_confirmation"
                    ? "The pending confirmation was expired and cannot authorize execution. UI cleanup was requested. Verify context before a new request; do not retry automatically."
                    : "If execution started it may still complete inside Revit. Verify model state before retrying; do not retry automatically.",
                context: new Dictionary<string, object>
                {
                    ["requestPhase"] = phase,
                    ["executionMayStillBeRunning"] = phase == "executing",
                    ["confirmationExpired"] = phase == "awaiting_confirmation"
                });
        }
    }

    public void Complete(CortexResult<object> result)
    {
        lock (_gate) { _phase = "completed"; _completion.TrySetResult(result); }
    }

    private sealed class Scope(Action restore) : IDisposable { public void Dispose() => restore(); }
}

public sealed class ConfirmationExpiredException : Exception
{
    public ConfirmationExpiredException() : base("The request expired before confirmation completed. No approval was granted; do not retry automatically.") { }
    public CortexResult<object> ToResult() => CortexResult<object>.Fail(CortexErrorCode.Timeout, Message,
        context: new Dictionary<string, object> { ["confirmationExpired"] = true, ["executionMayStillBeRunning"] = false });
}
