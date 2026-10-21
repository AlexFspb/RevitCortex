using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
namespace RevitCortex.Tools.Meta;
[ToolSafety(true, false)]
public sealed class GetConnectionStatusTool : ICortexTool
{
    public string Name => "get_connection_status";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Read cached primitive connection identity without waiting for Revit UI. This is not proof of UI responsiveness.";
    public CortexResult<object> Execute(JObject input, CortexSession session) => CortexResult<object>.Ok(session.ConnectionStatus());
}
