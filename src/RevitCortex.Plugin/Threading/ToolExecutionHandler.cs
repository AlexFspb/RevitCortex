using System;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class ToolExecutionHandler : IExternalEventHandler
{
    private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);
    private readonly object _stateLock = new object();
    private readonly AuditLogger _auditLogger;
    private int _executionId;
    private bool _hasPendingOrRunning;
    private CortexSession.DocumentContext _pendingDocumentContext;

    public ToolExecutionHandler(AuditLogger? auditLogger = null)
    {
        _auditLogger = auditLogger ?? new AuditLogger();
    }

    public ICortexTool? PendingTool { get; set; }
    public JObject? PendingInput { get; set; }
    public CortexSession? PendingSession { get; set; }
    public CortexResult<object>? Result { get; private set; }

    public void Execute(UIApplication app)
    {
        int myId;
        ICortexTool? tool;
        JObject? input;
        CortexSession? session;
        CortexSession.DocumentContext documentContext;

        lock (_stateLock)
        {
            myId = _executionId;
            tool = PendingTool;
            input = PendingInput;
            session = PendingSession;
            documentContext = _pendingDocumentContext;
        }

        var discarded = false;

        try
        {
            if (tool == null || input == null || session == null)
            {
                // Stale Raise: the state was cleared by a timeout and no new request
                // has been prepared. Never touch Result here — overwriting it could
                // clobber the response of a request that just completed but whose
                // dispatcher has not read Result yet.
                return;
            }

            // Validate immediately before execution on Revit's UI thread.
            // Never replay a queued command against a replacement document.
            var contextValid = session.IsCurrentDocumentContext(documentContext);
            if (contextValid && tool.RequiresDocument)
                contextValid = IsActiveDocument(app, documentContext.Document);
            var result = contextValid
                ? tool.Execute(input, session)
                : CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                    "The active document was closed or changed while this command was waiting. Nothing was executed.",
                    suggestion: "Check the active project, then issue a new command. The Cortex server remains available.");
            lock (_stateLock)
            {
                // Only store the result if this execution is still current
                // (not superseded by a timeout + new prepare).
                if (_executionId == myId)
                    Result = result;
                else
                    discarded = true;
            }

            if (discarded)
            {
                // The caller already received Timeout, but the tool ran to completion:
                // the model may differ from what the caller observed. Record the
                // divergence — the audit log is the source of truth.
                _auditLogger.LogWithPerf(tool.Name,
                    "completed_after_timeout (result discarded; model may have changed)",
                    result.Success, result.Error?.Code,
                    errorMessage: result.Error?.Message);
            }
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                if (_executionId == myId)
                    Result = ex is ConfirmationFailedException confirmation
                        ? confirmation.ToResult()
                        : CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Unhandled exception: {ex.Message}");
            }
        }
        finally
        {
            lock (_stateLock)
            {
                if (_executionId == myId)
                {
                    PendingTool = null;
                    PendingInput = null;
                    PendingSession = null;
                    _pendingDocumentContext = default;
                    _hasPendingOrRunning = false;
                    _resetEvent.Set();
                }
            }
        }
    }

    private static bool IsActiveDocument(UIApplication app, object? expected)
    {
        return expected is Autodesk.Revit.DB.Document document && document.IsValidObject
            && document.Equals(app?.ActiveUIDocument?.Document);
    }

    public bool TryPrepareExecution(ICortexTool tool, JObject input, CortexSession session,
        CortexSession.DocumentContext? documentContext = null)
    {
        lock (_stateLock)
        {
            if (_hasPendingOrRunning)
                return false;

            _executionId++;
            PendingTool = tool;
            PendingInput = input;
            PendingSession = session;
            _pendingDocumentContext = documentContext ?? session.CaptureDocumentContext();
            Result = null;
            _hasPendingOrRunning = true;
            _resetEvent.Reset();
            return true;
        }
    }

    public bool WaitForCompletion(int timeoutMs = 120000)
    {
        return _resetEvent.WaitOne(timeoutMs);
    }

    public void ClearPreparedExecution()
    {
        lock (_stateLock)
        {
            PendingTool = null;
            PendingInput = null;
            PendingSession = null;
            _pendingDocumentContext = default;
            _hasPendingOrRunning = false;
            _resetEvent.Set();
        }
    }

    public string GetName() => "RevitCortex Tool Execution";
}
