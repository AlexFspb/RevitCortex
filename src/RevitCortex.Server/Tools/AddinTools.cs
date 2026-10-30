using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Addins;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class AddinTools
{
    [McpServerTool(Name = "addin_list"), Description("List explicitly registered add-in commands, JSON schemas, modes, actual loaded module MVID/version/generation and cached Revit identity (including documentToken). Works while Revit is busy. Does not execute a command or rebind other tools. Verify this port/project before use. No arbitrary DLL/reflection commands.")]
    public static async Task<string> List(RevitConnectionManager revit, CancellationToken ct = default) =>
        (await revit.ExecuteAsync("addin_list", new JObject(), ct)).ToString();

    [McpServerTool(Name = "addin_execute"), Description("Admit ONE registered plugin operation on this fixed port. Returns operationId immediately; poll addin_get_operation. Busy rejects without queueing. requestId is a new UUID per intended operation; retry a lost admission response only with the SAME ID and identical payload. expectedContextJson must include instanceId, documentToken, documentGeneration and pluginGeneration from addin_list. Modes: preview=no effects; probeRollback=provider executes real changes then verifies rollback; commit=apply. Use only advertised modes/schemas. Provider owns transactions. Read-only and approval policies apply. A failed/unknown result does not prove rollback; never blindly rerun with a new ID.")]
    public static async Task<string> Execute(RevitConnectionManager revit, string requestId, string pluginId,
        string operationName, string expectedContextJson, string argumentsJson, string mode = "preview",
        int queueTimeoutSeconds = 30, CancellationToken ct = default) =>
        await Send(revit, "addin_execute", () => new JObject
        {
            ["requestId"] = requestId, ["pluginId"] = pluginId, ["operationName"] = operationName,
            ["expectedContext"] = AddinJson.Parse(expectedContextJson), ["arguments"] = AddinJson.Parse(argumentsJson),
            ["mode"] = mode, ["queueTimeoutSeconds"] = queueTimeoutSeconds
        }, ct);

    [McpServerTool(Name = "addin_get_operation"), Description("Inspect an accepted operation without waiting for the Revit UI. Supply its instanceId and operationId or requestId. Document switches do not hide outcomes. resultPage contains bounded JSON text; concatenate pages using nextOffset, then parse once. Retained results can expire; request tombstones still prevent duplicate execution. UnknownOutcome/InstanceChanged requires state verification, never an automatic replay.")]
    public static async Task<string> GetOperation(RevitConnectionManager revit, string instanceId,
        string? operationId = null, string? requestId = null, int resultOffset = 0, int resultCount = 16384, CancellationToken ct = default) =>
        (await revit.ExecuteAsync("addin_get_operation", Query(instanceId, operationId, requestId, resultOffset, resultCount), ct)).ToString();

    [McpServerTool(Name = "addin_cancel_operation"), Description("Cancel a queued operation, or REQUEST cooperative cancellation of a running provider that advertises it. Does not abort Revit/native code, undo a completed commit or close dialogs. Poll until a terminal receipt; cancelRequested is not proof of rollback. Works while UI is busy.")]
    public static async Task<string> CancelOperation(RevitConnectionManager revit, string instanceId,
        string? operationId = null, string? requestId = null, CancellationToken ct = default) =>
        (await revit.ExecuteAsync("addin_cancel_operation", Query(instanceId, operationId, requestId, 0, 16384), ct)).ToString();

    [McpServerTool(Name = "addin_reload"), Description("Request an advertised stable provider loader to reload its configured module while idle. No DLL path/reflection accepted. Uses the same asynchronous operation and requestId deduplication as addin_execute. Supply explicit instance/document/plugin generation from addin_list. Verify new loaded MVID/generation on completion. Unsupported loaders require restarting Revit; a failed reload disables the provider until registration is repaired.")]
    public static async Task<string> Reload(RevitConnectionManager revit, string requestId, string pluginId,
        string expectedContextJson, CancellationToken ct = default) =>
        await Send(revit, "addin_reload", () => new JObject { ["requestId"] = requestId, ["pluginId"] = pluginId,
            ["expectedContext"] = AddinJson.Parse(expectedContextJson) }, ct);

    private static async Task<string> Send(RevitConnectionManager revit, string method, Func<JObject> input, CancellationToken ct)
    {
        JObject parameters;
        try { parameters = input(); }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or AddinProtocolException)
        {
            return new JObject { ["success"] = false, ["error"] = new JObject { ["code"] = "InvalidInput",
                ["message"] = ex.Message, ["context"] = new JObject { ["reason"] = "InvalidJson", ["commandSent"] = false } } }.ToString();
        }
        return (await revit.ExecuteAsync(method, parameters, ct)).ToString();
    }

    private static JObject Query(string instanceId, string? operationId, string? requestId, int offset, int count)
    {
        var query = new JObject { ["instanceId"] = instanceId, ["resultOffset"] = offset, ["resultCount"] = count };
        if (operationId != null) query["operationId"] = operationId;
        if (requestId != null) query["requestId"] = requestId;
        return query;
    }
}
