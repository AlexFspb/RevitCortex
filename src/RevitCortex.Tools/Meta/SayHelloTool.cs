using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Meta;

[ToolSafety(true, false)]
public class SayHelloTool : ICortexTool
{
    public string Name => "say_hello";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Identify the connected Revit process, bridge port and active document.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var message = input["message"]?.ToString() ?? "Hello from RevitCortex!";
        // Runs through ExternalEvent on Revit's UI thread, including without a document.
        var document = session.Store.Get<Autodesk.Revit.DB.Document>("activeDocument");

        return CortexResult<object>.Ok(new
        {
            message,
            locale = session.DetectedLocale,
            toolCount = "RevitCortex is running",
            revitProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
            bridgePort = session.BridgePort,
            activeDocumentTitle = document?.IsValidObject == true ? document.Title : null
        });
    }
}
