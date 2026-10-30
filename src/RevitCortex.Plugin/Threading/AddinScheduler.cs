using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Addins;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin.Threading;

internal sealed class AddinScheduler(RevitThreadDispatcher dispatcher, CortexSession session) : IAddinScheduler
{
    public bool TrySchedule(Func<object?, CortexResult<object>> run, Action<CortexResult<object>> completed) =>
        dispatcher.TrySubmit(new Invocation(run), session, completed);

    public static void CheckApi(object? value, bool requiresDocument, bool mutatesDocument, CortexSession session)
    {
        if (value is not UIApplication app) throw new AddinProtocolException("ApiContextMissing", "Revit UI API context is unavailable.");
        var doc = app.ActiveUIDocument?.Document;
        if (requiresDocument && (doc == null || !doc.IsValidObject || !doc.Equals(session.Store.Get<Document>("activeDocument"))))
            throw new AddinProtocolException("DocumentChanged", "The requested document is not active in Revit.");
        if (doc?.IsModifiable == true) throw new AddinProtocolException("OpenTransaction", "A transaction is already open. Provider must own and finish its transactions.");
        if (mutatesDocument && doc?.IsReadOnly == true) throw new AddinProtocolException("ReadOnly", "The active Revit document is read-only.");
    }

    private sealed class Invocation(Func<object?, CortexResult<object>> run) : ICortexTool
    {
        public string Name => "addin_execute";
        public string Category => "Addins";
        public bool RequiresDocument => false;
        public bool IsDynamic => false;
        public string Description => "Run an already admitted add-in operation in the shared Revit event.";
        public CortexResult<object> Execute(JObject input, CortexSession context) => run(context.Store.Get<object>("uiApplication"));
    }
}
