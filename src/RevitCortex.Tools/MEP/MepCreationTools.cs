using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.MEP;

// Native point-creation pattern adapted from Demolinator/revit-mcp-server (MIT),
// revit_mcp/mep.py, file commit 0b8d5cebf0d23a21a2ffccee9e85bba614d71c7c.
[ToolSafety(false, false)]
public sealed class CreatePipeTool : ICortexTool
{
    public string Name => "create_pipe";
    public string Category => "MEP";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Preview or atomically create one straight native pipe using explicit system/type/level IDs and millimeter coordinates/dimensions.";
    public CortexResult<object> Execute(JObject input, CortexSession session) => MepCreation.Execute(input, session, pipe: true);
}

[ToolSafety(false, false)]
public sealed class CreateDuctTool : ICortexTool
{
    public string Name => "create_duct";
    public string Category => "MEP";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Preview or atomically create one straight native duct using explicit IDs, exact shape and millimeter coordinates/dimensions.";
    public CortexResult<object> Execute(JObject input, CortexSession session) => MepCreation.Execute(input, session, pipe: false);
}

internal static class MepCreation
{
    internal static CortexResult<object> Execute(JObject input, CortexSession session, bool pipe)
    {
        var writeEntered = false;
        try
        {
            var request = MepCreationInput.Parse(input, pipe);
            var (doc, error) = ToolHelpers.RequireDocument(session);
            if (doc == null) return error!;
            if (doc.IsFamilyDocument || doc.IsReadOnly || doc.IsModifiable)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "Native MEP creation requires a writable project with no open transaction.");
            var context = session.CaptureDocumentContext();
            var version = session.DocumentVersion;
            ValidateDocument(doc, request);
            if (request.DryRun)
            {
                var preview = RequestReceipt(request);
                preview["dryRun"] = true; preview["transactionState"] = "none"; preview["modelChanged"] = false;
                preview["nativePlacementVerified"] = false;
                preview["note"] = "Preview checks input, explicit IDs, type shape, coordinate and length limits. Native size/routing feasibility is verified only during the atomic write.";
                return CortexResult<object>.Ok(preview);
            }
            if (!session.RequestConfirmation(pipe ? "create native pipe" : "create native duct", 1,
                "Create one independent straight segment. Any warning, error or verification mismatch rolls back this request."))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "MEP creation was not approved; no changes were made.",
                    context: new Dictionary<string, object> { ["transactionState"] = "none", ["modelChanges"] = "none" });
            if (!session.IsCurrentDocumentContext(context) || !doc.IsValidObject ||
                !doc.Equals(session.Store.Get<object>("activeDocument")) || doc.IsModifiable || doc.IsReadOnly || session.DocumentVersion != version)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "The active project or model changed during confirmation. Inspect the document before a new preview.",
                    context: new Dictionary<string, object> { ["transactionState"] = "none", ["modelChanges"] = "none" });
            writeEntered = true;
            return Write(doc, request);
        }
        catch (ConfirmationFailedException ex) { return ex.ToResult(); }
        catch (ConfirmationExpiredException ex) { return ex.ToResult(); }
        catch (DocumentContextChangedException ex) { return ex.ToResult(); }
        catch (Exception ex)
        {
            var code = writeEntered ? CortexErrorCode.Unknown : ex is ArgumentException ? CortexErrorCode.InvalidInput : CortexErrorCode.Unknown;
            return CortexResult<object>.Fail(code, Bound(ex.Message),
                suggestion: writeEntered ? "Outcome is unknown. Verify model state before another request; do not repeat automatically." :
                    "Inspect the explicit system/type/level IDs and dimensions, then use dryRun=true. No fallback types are selected.",
                context: new Dictionary<string, object> { ["transactionState"] = writeEntered ? "unknown" : "none", ["modelChanges"] = writeEntered ? "unknown" : "none" });
        }
    }

    private static void ValidateDocument(Document doc, MepCreationInput request)
    {
        if (doc.GetElement(new ElementId(request.LevelId)) is not Level)
            throw new ArgumentException("levelId does not identify a Level in the active project.");
        var system = doc.GetElement(new ElementId(request.SystemTypeId));
        var type = doc.GetElement(new ElementId(request.TypeId));
        if (request.IsPipe)
        {
            if (system is not PipingSystemType || type is not PipeType)
                throw new ArgumentException("systemTypeId/pipeTypeId must identify a piping system type and a pipe type in the active project.");
        }
        else
        {
            if (system is not MechanicalSystemType || type is not DuctType ductType)
                throw new ArgumentException("systemTypeId/ductTypeId must identify a mechanical system type and a duct type in the active project.");
            var expected = request.Shape switch { "round" => ConnectorProfileType.Round, "rectangular" => ConnectorProfileType.Rectangular, _ => ConnectorProfileType.Oval };
            if (ductType.Shape != expected) throw new ArgumentException("The requested shape does not match the explicit duct type. No replacement type will be selected.");
        }
        if (!XYZ.IsWithinLengthLimits(Point(request.StartMm)) || !XYZ.IsWithinLengthLimits(Point(request.EndMm)))
            throw new ArgumentException("The requested points exceed Revit design limits.");
        if (MepCreationInput.Feet(request.LengthMm) < doc.Application.ShortCurveTolerance)
            throw new ArgumentException("The requested segment is shorter than Revit ShortCurveTolerance.");
    }

    private static CortexResult<object> Write(Document doc, MepCreationInput request)
    {
        using var group = new TransactionGroup(doc, "RevitCortex: Create Native MEP Segment");
        using var tx = new Transaction(doc, request.IsPipe ? "RevitCortex: Create Pipe" : "RevitCortex: Create Duct");
        var groupAttempted = false; var txAttempted = false;
        ScriptFailureHandling.FailureCapture? failures = null;
        try
        {
            ToolRequestLifetime.Current?.Expiration.ThrowIfCancellationRequested();
            groupAttempted = true;
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("TransactionGroup did not start.");
            txAttempted = true;
            if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaction did not start.");
            failures = ScriptFailureHandling.Configure(tx, rollbackOnWarnings: true);
            MEPCurve curve = request.IsPipe
                ? Pipe.Create(doc, new ElementId(request.SystemTypeId), new ElementId(request.TypeId), new ElementId(request.LevelId), Point(request.StartMm), Point(request.EndMm))
                : Duct.Create(doc, new ElementId(request.SystemTypeId), new ElementId(request.TypeId), new ElementId(request.LevelId), Point(request.StartMm), Point(request.EndMm));
            if (curve == null) throw new InvalidOperationException("Revit did not return the requested native MEP segment.");
            foreach (var dimension in Dimensions(request)) Set(curve, dimension.Id, dimension.ValueMm);
            doc.Regenerate();
            Verify(curve, request);
            var commit = tx.Commit();
            if (commit != TransactionStatus.Committed) throw new InvalidOperationException($"Revit did not commit the transaction: {commit}.");
            Verify(curve, request);
            ToolRequestLifetime.Current?.Expiration.ThrowIfCancellationRequested();
            var receipt = RequestReceipt(request);
            receipt["dryRun"] = false; receipt["transactionState"] = "committed"; receipt["modelChanged"] = true;
            receipt["elementId"] = curve.Id.Value; receipt["uniqueId"] = curve.UniqueId;
            receipt["placementVerified"] = true; receipt["dimensionsVerified"] = true;
            receipt["actualStartPoint"] = Coordinates(((LocationCurve)curve.Location).Curve.GetEndPoint(0));
            receipt["actualEndPoint"] = Coordinates(((LocationCurve)curve.Location).Curve.GetEndPoint(1));
            if (group.Assimilate() != TransactionStatus.Committed) throw new InvalidOperationException("TransactionGroup did not assimilate successfully.");
            return CortexResult<object>.Ok(receipt);
        }
        catch (Exception ex)
        {
            // No element/model reads after a failed regeneration. Rollback/status operations only.
            var cleanup = new List<string>(); var txState = "none"; var groupState = "none";
            try
            {
                if (txAttempted)
                {
                    var status = tx.GetStatus();
                    if (status == TransactionStatus.Started) status = tx.RollBack();
                    txState = status.ToString();
                }
            }
            catch (Exception rollback) { txState = "unknown"; cleanup.Add(Bound(rollback.Message)); }
            try
            {
                if (groupAttempted)
                {
                    var status = group.GetStatus();
                    if (status == TransactionStatus.Started) status = group.RollBack();
                    groupState = status.ToString();
                }
            }
            catch (Exception rollback) { groupState = "unknown"; cleanup.Add(Bound(rollback.Message)); }
            var rolledBack = groupState == TransactionStatus.RolledBack.ToString();
            var noChanges = !groupAttempted && !txAttempted;
            return CortexResult<object>.Fail(ex is OperationCanceledException ? CortexErrorCode.Cancelled : CortexErrorCode.TransactionFailed,
                rolledBack ? "MEP creation failed; all changes from this request were rolled back. " + Bound(ex.Message) :
                    noChanges ? "MEP creation did not start. " + Bound(ex.Message) :
                    "MEP creation failed; rollback is not confirmed. " + Bound(ex.Message),
                suggestion: "Read primitive failure diagnostics. Verify model state before retrying; do not automatically repeat an uncertain request.",
                context: new Dictionary<string, object>
                {
                    ["transactionState"] = txState, ["transactionGroupState"] = groupState,
                    ["rollbackConfirmed"] = rolledBack, ["modelChanges"] = rolledBack || noChanges ? "none" : "unknown",
                    ["createdElementIds"] = Array.Empty<long>(), ["rollbackErrors"] = cleanup,
                    ["failures"] = failures?.Failures ?? Array.Empty<Dictionary<string, object>>(),
                    ["omittedFailures"] = failures?.OmittedFailures ?? 0,
                    ["diagnosticReportPath"] = failures?.DiagnosticReportPath ?? "(none)"
                });
        }
    }

    private static void Verify(MEPCurve curve, MepCreationInput request)
    {
        if (!curve.IsValidObject || curve.GetTypeId().Value != request.TypeId || curve.ReferenceLevel?.Id.Value != request.LevelId)
            throw new InvalidOperationException("The actual MEP type or reference level differs from the request.");
        var system = curve.get_Parameter(request.IsPipe ? BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM : BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM);
        if (system?.StorageType != StorageType.ElementId || system.AsElementId().Value != request.SystemTypeId)
            throw new InvalidOperationException("The actual MEP system type differs from the request.");
        if (curve.Location is not LocationCurve location || location.Curve is not Line line ||
            !((Close(line.GetEndPoint(0), request.StartMm) && Close(line.GetEndPoint(1), request.EndMm)) ||
              (Close(line.GetEndPoint(1), request.StartMm) && Close(line.GetEndPoint(0), request.EndMm))))
            throw new InvalidOperationException("The actual native segment endpoints differ from the request.");
        foreach (var dimension in Dimensions(request))
        {
            var p = curve.get_Parameter(dimension.Id);
            if (p?.StorageType != StorageType.Double)
                throw new InvalidOperationException(MepSizeDiagnostic.Message(dimension.Id.ToString(), dimension.ValueMm, null,
                    "required size parameter is unavailable or has the wrong storage type during verification"));
            var actual = p.AsDouble();
            if (!Same(actual, MepCreationInput.Feet(dimension.ValueMm)))
                throw new InvalidOperationException(MepSizeDiagnostic.Message(dimension.Id.ToString(), dimension.ValueMm, actual,
                    "actual size differs after regeneration/commit"));
        }
        if (!request.IsPipe)
        {
            var expected = request.Shape switch { "round" => ConnectorProfileType.Round, "rectangular" => ConnectorProfileType.Rectangular, _ => ConnectorProfileType.Oval };
            if (curve is not Duct duct || duct.DuctType.Shape != expected)
                throw new InvalidOperationException("The actual duct shape differs from the request.");
        }
    }
    private static void Set(MEPCurve curve, BuiltInParameter id, double mm)
    {
        var p = curve.get_Parameter(id);
        var value = MepCreationInput.Feet(mm);
        if (p?.StorageType != StorageType.Double)
            throw new InvalidOperationException(MepSizeDiagnostic.Message(id.ToString(), mm, null,
                "required size parameter is unavailable or has the wrong storage type"));
        var actual = p.AsDouble();
        if (Same(actual, value)) return;
        if (p.IsReadOnly)
            throw new InvalidOperationException(MepSizeDiagnostic.Message(id.ToString(), mm, actual, "parameter is read-only"));
        var changed = p.Set(value);
        actual = p.AsDouble();
        if (!changed || !Same(actual, value))
            throw new InvalidOperationException(MepSizeDiagnostic.Message(id.ToString(), mm, actual,
                changed ? "Revit did not retain the requested size" : "Parameter.Set returned false"));
    }
    private static IEnumerable<(BuiltInParameter Id, double ValueMm)> Dimensions(MepCreationInput request)
    {
        if (request.Shape == "round") yield return (request.IsPipe ? BuiltInParameter.RBS_PIPE_DIAMETER_PARAM : BuiltInParameter.RBS_CURVE_DIAMETER_PARAM, request.DiameterMm!.Value);
        else
        {
            yield return (BuiltInParameter.RBS_CURVE_WIDTH_PARAM, request.WidthMm!.Value);
            yield return (BuiltInParameter.RBS_CURVE_HEIGHT_PARAM, request.HeightMm!.Value);
        }
    }
    private static JObject RequestReceipt(MepCreationInput request) => new()
    {
        ["kind"] = request.IsPipe ? "pipe" : "duct", ["systemTypeId"] = request.SystemTypeId,
        [request.IsPipe ? "pipeTypeId" : "ductTypeId"] = request.TypeId, ["levelId"] = request.LevelId,
        ["shape"] = request.Shape, ["startPoint"] = new JArray(request.StartMm), ["endPoint"] = new JArray(request.EndMm),
        ["diameterMm"] = request.DiameterMm, ["widthMm"] = request.WidthMm, ["heightMm"] = request.HeightMm,
        ["coordinateUnit"] = "mm", ["coordinateSystem"] = "Revit internal model origin",
        ["lengthMm"] = request.LengthMm, ["connectionsRequested"] = false,
        ["noteConnections"] = "This tool does not request connector joins, fittings, routing, slope adjustment or synchronization. Inspect connectivity separately."
    };
    private static XYZ Point(double[] mm) => new(MepCreationInput.Feet(mm[0]), MepCreationInput.Feet(mm[1]), MepCreationInput.Feet(mm[2]));
    private static JArray Coordinates(XYZ p) => new(MepCreationInput.Millimeters(p.X), MepCreationInput.Millimeters(p.Y), MepCreationInput.Millimeters(p.Z));
    private static bool Close(XYZ p, double[] mm) => p.DistanceTo(Point(mm)) <= MepCreationInput.GeometryToleranceFt;
    private static bool Same(double actual, double expected) => Math.Abs(actual - expected) <= Math.Max(1e-8, Math.Abs(expected) * 1e-8);
    private static string Bound(string text) => text.Length > 2048 ? text[..2048] : text;
}
