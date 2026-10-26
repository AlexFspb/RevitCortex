using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;
namespace RevitCortex.Tools.Meta;
[ToolSafety(false, false)]
public sealed class EnsureServiceDocumentTool : ICortexTool
{
    public string Name => "ensure_service_document";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Create a private empty service project only if no document is active. Never replace an active project. Used before a first script in an empty Revit.";
    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        // Automatic C# bootstrap must not create files for disabled or sandbox-rejected code.
        if (input["code"] != null)
        {
            if (session.CanPrepareScript?.Invoke() != true || !CortexSettings.Load().EnableCodeExecution)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied, "Code execution is disabled; no service project was created.");
            var code = input.Value<string>("code");
            if (string.IsNullOrWhiteSpace(code)) return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");
            var error = ScriptTransactionMode.Validate(input.Value<string>("transactionMode"), input.Value<bool?>("strictWarnings") ?? false)
                ?? CodeSandbox.Validate(code!);
            if (error != null) return error;
        }
        return session.EnsureServiceDocument?.Invoke(input["code"] != null)
            ?? CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "Revit application is not ready; no service project was created.");
    }
}
