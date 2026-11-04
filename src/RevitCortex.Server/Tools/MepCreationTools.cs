using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class MepCreationTools
{
    [McpServerTool(Name = "create_pipe"), Description("Preview or create ONE straight native Revit pipe on this fixed port. Supply explicit active-document IDs of a PipingSystemType, PipeType and Level; inspect them first. Never selects a fallback type/system. startPoint/endPoint are three coordinates in mm relative to Revit's internal origin; get_location_curves returns feet, so multiply its coordinates by 304.8. diameterMm is positive. Default dryRun=true makes no changes; false creates with strict warning/error rollback and verifies type, system type, level, endpoints and size before finishing the transaction group. Does not request fittings, connector joins/rewiring, save or synchronization; inspect native connectivity separately. Verify actual state after timeout or unknown rollback; never blindly repeat a write.")]
    public static async Task<string> CreatePipe(
        RevitConnectionManager revit,
        [Description("ID of a PipingSystemType in the active project")] long systemTypeId,
        [Description("ID of a PipeType in the active project")] long pipeTypeId,
        [Description("ID of the reference Level")] long levelId,
        [Description("Start [x,y,z] in mm from Revit internal origin")] double[] startPoint,
        [Description("End [x,y,z] in mm from Revit internal origin")] double[] endPoint,
        [Description("Positive pipe diameter in mm")] double diameterMm,
        [Description("Preview without changes. Default true; false applies")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var input = new JObject
        {
            ["systemTypeId"] = systemTypeId, ["pipeTypeId"] = pipeTypeId, ["levelId"] = levelId,
            ["startPoint"] = new JArray(startPoint), ["endPoint"] = new JArray(endPoint),
            ["diameterMm"] = diameterMm, ["dryRun"] = dryRun
        };
        return (await revit.ExecuteAsync("create_pipe", input, ct)).ToString();
    }

    [McpServerTool(Name = "create_duct"), Description("Preview or create ONE straight native rigid Revit duct on this fixed port. Supply explicit active-document IDs of a MechanicalSystemType, DuctType and Level. shape must be round, rectangular or oval and must match the chosen type. Round requires only diameterMm; rectangular/oval require only widthMm and heightMm. Points and dimensions are mm; points use Revit internal origin. get_location_curves returns feet: multiply its coordinates by 304.8. Default dryRun=true makes no changes. false uses strict warning/error rollback and verifies actual type, system type, level, axis and dimensions before finishing the group. No first-type fallback; does not request fittings, connector joins/rewiring or save. Inspect native connectivity separately. Preview cannot prove native placement success. Inspect state before retry after timeout or uncertain rollback.")]
    public static async Task<string> CreateDuct(
        RevitConnectionManager revit,
        [Description("ID of a MechanicalSystemType in the active project")] long systemTypeId,
        [Description("ID of a DuctType in the active project")] long ductTypeId,
        [Description("ID of the reference Level")] long levelId,
        [Description("Start [x,y,z] in mm from Revit internal origin")] double[] startPoint,
        [Description("End [x,y,z] in mm from Revit internal origin")] double[] endPoint,
        [Description("round, rectangular or oval; must match the selected DuctType")] string shape,
        [Description("Required only for round ducts, in mm")] double? diameterMm = null,
        [Description("Required for rectangular/oval ducts, in mm")] double? widthMm = null,
        [Description("Required for rectangular/oval ducts, in mm")] double? heightMm = null,
        [Description("Preview without changes. Default true; false applies")] bool dryRun = true,
        CancellationToken ct = default)
    {
        var input = new JObject
        {
            ["systemTypeId"] = systemTypeId, ["ductTypeId"] = ductTypeId, ["levelId"] = levelId,
            ["startPoint"] = new JArray(startPoint), ["endPoint"] = new JArray(endPoint),
            ["shape"] = shape, ["dryRun"] = dryRun
        };
        if (diameterMm.HasValue) input["diameterMm"] = diameterMm.Value;
        if (widthMm.HasValue) input["widthMm"] = widthMm.Value;
        if (heightMm.HasValue) input["heightMm"] = heightMm.Value;
        return (await revit.ExecuteAsync("create_duct", input, ct)).ToString();
    }
}
