using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class ToolExecutionHandler : IExternalEventHandler
{
    private readonly object _stateLock = new();
    private readonly AuditLogger _auditLogger;
    private ToolRequestLifetime? _request;
    private bool _executing;
    private CortexSession.DocumentContext _pendingDocumentContext;
    public ToolExecutionHandler(AuditLogger? auditLogger = null) => _auditLogger = auditLogger ?? new AuditLogger();
    public ICortexTool? PendingTool { get; private set; }
    public JObject? PendingInput { get; private set; }
    public CortexSession? PendingSession { get; private set; }
    public CortexResult<object>? Result { get; private set; }

    public void Execute(UIApplication app)
    {
        ToolRequestLifetime request;
        ICortexTool tool;
        JObject input;
        CortexSession session;
        CortexSession.DocumentContext context;
        lock (_stateLock)
        {
            // Stale Raise: never overwrite another request's result or run reentrantly.
            if (_request == null || _executing || PendingTool == null || PendingInput == null || PendingSession == null) return;
            request = _request; tool = PendingTool; input = PendingInput; session = PendingSession;
            context = _pendingDocumentContext;
            _executing = true;
        }
        CortexResult<object> result;
        try
        {
            if (!request.TryStart()) result = request.TimeoutResult(tool.Name, 0);
            else
            {
                request.OwnerHandle = app?.MainWindowHandle ?? IntPtr.Zero;
                using var scope = request.Enter();
                var valid = session.IsCurrentDocumentContext(context);
                if (valid && tool.RequiresDocument) valid = IsActiveDocument(app!, context.Document);
                result = valid ? tool.Execute(input, session) : CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                    "The active document was closed or changed while this command was waiting. Nothing was executed.",
                    suggestion: "Verify the active project before issuing a new request; do not retry automatically.");
            }
        }
        catch (ConfirmationExpiredException ex) { result = ex.ToResult(); }
        catch (ConfirmationFailedException ex) { result = ex.ToResult(); }
        catch (Exception ex) { result = CortexResult<object>.Fail(CortexErrorCode.Unknown, $"Unhandled exception: {ex.Message}"); }
        lock (_stateLock)
        {
            // Each waiter reads its own request.Completion, never this shared compatibility property.
            Result = result;
            request.Complete(result);
            PendingTool = null; PendingInput = null; PendingSession = null;
            _pendingDocumentContext = default;
            _request = null; _executing = false;
        }
        if (request.IsExpired)
            _auditLogger.LogWithPerf(tool.Name, "completed_after_timeout (inspect result and model state)",
                result.Success, result.Error?.Code, errorMessage: result.Error?.Message);
    }

    private static bool IsActiveDocument(UIApplication app, object? expected) =>
        expected is Autodesk.Revit.DB.Document document && document.IsValidObject && document.Equals(app?.ActiveUIDocument?.Document);

    public bool TryPrepareExecution(ICortexTool tool, JObject input, CortexSession session,
        CortexSession.DocumentContext? documentContext = null) => TryPrepareExecution(tool, input, session, out _, documentContext);

    public bool TryPrepareExecution(ICortexTool tool, JObject input, CortexSession session,
        out ToolRequestLifetime request, CortexSession.DocumentContext? documentContext = null)
    {
        lock (_stateLock)
        {
            request = _request ?? new ToolRequestLifetime();
            if (_request != null) return false;
            _request = request;
            PendingTool = tool; PendingInput = input; PendingSession = session;
            _pendingDocumentContext = documentContext ?? session.CaptureDocumentContext();
            Result = null;
            return true;
        }
    }

    // Only a rejected Raise can release a queued slot without an ExternalEvent drain.
    public void RejectPreparedExecution(ToolRequestLifetime request)
    {
        lock (_stateLock)
        {
            if (!ReferenceEquals(request, _request) || _executing) return;
            request.Complete(CortexResult<object>.Fail(CortexErrorCode.Timeout, "Revit rejected the event request; nothing was executed."));
            PendingTool = null; PendingInput = null; PendingSession = null;
            _pendingDocumentContext = default; _request = null;
        }
    }
    public string GetName() => "RevitCortex Tool Execution";
}
