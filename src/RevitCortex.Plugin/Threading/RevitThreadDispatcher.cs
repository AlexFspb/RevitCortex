using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

public class RevitThreadDispatcher
{
    private readonly ToolExecutionHandler _handler;
    private readonly ExternalEvent _externalEvent;
    private readonly object _lock = new object();

    public RevitThreadDispatcher(ToolExecutionHandler handler, ExternalEvent externalEvent)
    {
        _handler = handler;
        _externalEvent = externalEvent;
    }

    public CortexResult<object> Execute(ICortexTool tool, JObject input, CortexSession session,
        int timeoutMs = 120000, CortexSession.DocumentContext? documentContext = null)
    {
        // H5: only the prepare + Raise pair must be atomic against other requests; the
        // shared ToolExecutionHandler already rejects a concurrent request via
        // TryPrepareExecution. Holding _lock across WaitForCompletion (up to 120s) would
        // serialize *every* tool call behind a single slow/hung operation, so the wait is
        // done OUTSIDE the lock.
        ToolRequestLifetime request;
        lock (_lock)
        {
            if (!_handler.TryPrepareExecution(tool, input, session, out request, documentContext))
            {
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Tool '{tool.Name}' could not start because a previous Revit event is still pending or running",
                    suggestion: "A previous request still owns the Revit event. Inspect its confirmation/status; do not submit duplicate commands.",
                    context: new System.Collections.Generic.Dictionary<string, object> { ["activeRequestPhase"] = request.Phase, ["activeRequestExpired"] = request.IsExpired, ["thisRequestStarted"] = false });
            }

            ExternalEventRequest raiseResult;
            try { raiseResult = _externalEvent.Raise(); }
            catch { _handler.RejectPreparedExecution(request); throw; }
            if (raiseResult != ExternalEventRequest.Accepted)
            {
                _handler.RejectPreparedExecution(request);
                return CortexResult<object>.Fail(CortexErrorCode.Timeout,
                    $"Revit rejected the event request: {raiseResult}",
                    suggestion: "Revit may be busy with another operation. Try again.");
            }
        }

        if (!request.Completion.Wait(timeoutMs))
        {
            // Never release a pending/running event on timeout. Cancellation closes a pending
            // confirmation through its Dispatcher; executing code may still complete.
            return request.Expire(tool.Name, timeoutMs);
        }
        return request.Completion.Result;
    }
}
