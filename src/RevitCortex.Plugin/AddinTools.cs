using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Plugin;

// Registry/settings entries. CortexRouter dispatches through the guarded operation service.
// Direct tool execution never invokes a provider.
[ToolSafety(false)]
public abstract class AddinTool : ICortexTool
{
    public abstract string Name { get; }
    public string Category => "Addins";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Registered add-in commands: " + Name;
    public CortexResult<object> Execute(JObject input, CortexSession session) =>
        CortexResult<object>.Fail(CortexErrorCode.PermissionDenied, "Use the guarded add-in router.");
}
public sealed class AddinListTool : AddinTool { public override string Name => "addin_list"; }
public sealed class AddinExecuteTool : AddinTool { public override string Name => "addin_execute"; }
public sealed class AddinGetOperationTool : AddinTool { public override string Name => "addin_get_operation"; }
public sealed class AddinCancelOperationTool : AddinTool { public override string Name => "addin_cancel_operation"; }
public sealed class AddinReloadTool : AddinTool { public override string Name => "addin_reload"; }
