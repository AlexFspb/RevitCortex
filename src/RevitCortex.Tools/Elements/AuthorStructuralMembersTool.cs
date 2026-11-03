using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Atomic placement of straight beams and vertical columns from loaded families.
/// Based on the structural authoring idea in woodhouse711/RevitCortex, commit
/// 6468c9191e00871a53a233282907a1ea7adb909f (MIT). This implementation intentionally
/// uses whole-batch rollback rather than retaining individual failed placements.
/// </summary>
[ToolSafety(false, false)]
public sealed class AuthorStructuralMembersTool : ICortexTool
{
    public string Name => "author_structural_members";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Preview or atomically create straight structural beams and vertical columns from loaded families. Coordinates and numeric parameters use Revit internal units. All changes roll back on any warning, error or verification mismatch.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var writeEntered = false;
        try
        {
            var request = StructuralMemberInput.Parse(input);
            var (doc, documentError) = ToolHelpers.RequireDocument(session);
            if (doc == null) return documentError!;
            if (doc.IsFamilyDocument || doc.IsReadOnly || doc.IsModifiable)
                return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    "Structural authoring requires a writable project with no open transaction.");
            var context = session.CaptureDocumentContext();
            var version = session.DocumentVersion;
            var plans = Plan(doc, request);
            if (request.DryRun)
                return CortexResult<object>.Ok(new JObject
                {
                    ["transactionState"] = "none", ["dryRun"] = true, ["modelChanged"] = false,
                    ["units"] = "Revit internal units; coordinates and lengths in feet",
                    ["previewOnly"] = true, ["nativePlacementVerified"] = false,
                    ["instanceParametersVerified"] = false,
                    ["note"] = "Preview checks input, loaded types, levels and type parameter compatibility. Placement, instance parameters and native failures are verified only during the atomic write.",
                    ["results"] = new JArray(plans.Select(p => new JObject
                    {
                        ["sourceId"] = p.Spec.SourceId, ["status"] = "planned", ["role"] = p.Spec.Role,
                        ["family"] = p.Spec.Family, ["typeName"] = p.Spec.TypeName,
                        ["typeWillBeCreated"] = p.Target == null, ["levelName"] = p.BaseLevel.Name,
                        ["p0_ft"] = new JArray(p.Spec.P0), ["p1_ft"] = new JArray(p.Spec.P1)
                    }))
                });
            if (!session.RequestConfirmation("create structural beams and columns", request.Members.Count,
                "The whole batch is atomic. Any warning, error or verification mismatch rolls back all requested placements and new types."))
                return CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                    "Structural authoring was not approved; no model changes were made.",
                    context: new Dictionary<string, object> { ["transactionState"] = "none", ["modelChanged"] = false });
            if (!session.IsCurrentDocumentContext(context) || !doc.IsValidObject ||
                !doc.Equals(session.Store.Get<object>("activeDocument")) || doc.IsModifiable || doc.IsReadOnly || session.DocumentVersion != version)
                return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                    "The project or model changed during confirmation. No changes were made; inspect the current document before a new preview.");
            writeEntered = true;
            return Write(doc, plans, request);
        }
        catch (ConfirmationFailedException ex) { return ex.ToResult(); }
        catch (ConfirmationExpiredException ex) { return ex.ToResult(); }
        catch (DocumentContextChangedException ex) { return ex.ToResult(); }
        catch (ArgumentException ex)
        {
            return CortexResult<object>.Fail(writeEntered ? CortexErrorCode.TransactionFailed : CortexErrorCode.InvalidInput, Bound(ex.Message),
                suggestion: writeEntered ? "Model outcome is unknown. Verify model state before issuing another request; do not repeat automatically." :
                    "Inspect loaded family types and parameter names, then run dryRun=true. Coordinates and numeric parameter values must use Revit internal units.",
                context: new Dictionary<string, object> { ["transactionState"] = writeEntered ? "unknown" : "none", ["modelChanges"] = writeEntered ? "unknown" : "none" });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, Bound(ex.Message),
                context: new Dictionary<string, object> { ["transactionState"] = writeEntered ? "unknown" : "none", ["modelChanges"] = writeEntered ? "unknown" : "none" });
        }
    }

    private static List<PlanEntry> Plan(Document doc, StructuralMemberInput request)
    {
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.ProjectElevation).ToArray();
        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Where(s => s.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming ||
                        s.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns).ToArray();
        if (levels.Length == 0) throw new ArgumentException("The project contains no levels.");
        var result = new List<PlanEntry>();
        foreach (var member in request.Members)
        {
            var p0 = Point(member.P0); var p1 = Point(member.P1);
            if (!XYZ.IsWithinLengthLimits(p0) || !XYZ.IsWithinLengthLimits(p1))
                throw new ArgumentException($"{member.SourceId}: coordinates exceed Revit design limits.");
            if (p0.DistanceTo(p1) < doc.Application.ShortCurveTolerance)
                throw new ArgumentException($"{member.SourceId}: member is shorter than Revit ShortCurveTolerance.");
            var family = symbols.Where(s => s.FamilyName.Equals(member.Family, StringComparison.OrdinalIgnoreCase)).ToArray();
            var target = Unique(family.Where(s => s.Name.Equals(member.TypeName, StringComparison.OrdinalIgnoreCase)), "target type", member);
            var template = target ?? Unique(family.Where(s => s.Name.Equals(member.BaseType, StringComparison.OrdinalIgnoreCase)), "base type", member)
                ?? throw new ArgumentException($"{member.SourceId}: loaded family/base type was not found.");
            var category = member.Role == "beam" ? BuiltInCategory.OST_StructuralFraming : BuiltInCategory.OST_StructuralColumns;
            if (template.Category?.Id.Value != (long)category)
                throw new ArgumentException($"{member.SourceId}: the family category does not match role {member.Role}.");
            foreach (var parameter in member.TypeParams.Properties())
            {
                var p = Parameter(template, parameter.Name);
                ValidateValue(p, parameter.Value);
                if (target != null && !Matches(p, parameter.Value))
                    throw new ArgumentException($"{member.SourceId}: existing type '{member.TypeName}' has different '{parameter.Name}'. Existing types are never modified.");
                if (target == null && p.IsReadOnly && !Matches(p, parameter.Value))
                    throw new ArgumentException($"{member.SourceId}: type parameter '{parameter.Name}' is read-only.");
            }
            var low = Math.Min(member.P0[2], member.P1[2]); var high = Math.Max(member.P0[2], member.P1[2]);
            var baseLevel = member.LevelName == null ? LevelFor(levels, low) :
                levels.SingleOrDefault(l => l.Name.Equals(member.LevelName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{member.SourceId}: level '{member.LevelName}' was not found.");
            result.Add(new PlanEntry(member, template, target, baseLevel, LevelFor(levels, high)));
        }
        return result;
    }

    private static CortexResult<object> Write(Document doc, List<PlanEntry> plans, StructuralMemberInput request)
    {
        // Keep the group open through post-commit readback and response construction.
        // If either fails, committed transaction effects can still be rolled back.
        using var group = new TransactionGroup(doc, "RevitCortex: Author Structural Members");
        using var tx = new Transaction(doc, "RevitCortex: Place Structural Members");
        ScriptFailureHandling.FailureCapture? failures = null;
        var groupAttempted = false; var txAttempted = false;
        var sourceId = "";
        var created = new List<(PlanEntry Plan, FamilyInstance Instance, FamilySymbol Symbol)>();
        var createdTypes = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ToolRequestLifetime.Current?.Expiration.ThrowIfCancellationRequested();
            groupAttempted = true;
            if (group.Start() != TransactionStatus.Started) throw new InvalidOperationException("TransactionGroup did not start.");
            txAttempted = true;
            if (tx.Start() != TransactionStatus.Started) throw new InvalidOperationException("Transaction did not start.");
            failures = ScriptFailureHandling.Configure(tx, rollbackOnWarnings: true);
            foreach (var plan in plans)
            {
                ToolRequestLifetime.Current?.Expiration.ThrowIfCancellationRequested();
                sourceId = plan.Spec.SourceId;
                var symbol = plan.Target;
                if (symbol == null && !createdTypes.TryGetValue(plan.Spec.TypeKey, out symbol))
                {
                    symbol = plan.Template.Duplicate(plan.Spec.TypeName) as FamilySymbol
                        ?? throw new InvalidOperationException("Revit did not duplicate the requested family type.");
                    Apply(symbol, plan.Spec.TypeParams);
                    createdTypes.Add(plan.Spec.TypeKey, symbol);
                }
                if (!symbol.IsActive) symbol.Activate();
                doc.Regenerate();
                var instance = Place(doc, plan, symbol);
                Apply(instance, plan.Spec.InstanceParams);
                instance.Pinned = request.Pin;
                created.Add((plan, instance, symbol));
            }
            doc.Regenerate();
            foreach (var item in created)
            {
                sourceId = item.Plan.Spec.SourceId;
                Verify(item.Plan, item.Instance, item.Symbol, request.Pin);
            }
            var commit = tx.Commit();
            if (commit != TransactionStatus.Committed)
                throw new InvalidOperationException($"Revit did not commit the transaction: {commit}.");
            foreach (var item in created)
            {
                sourceId = item.Plan.Spec.SourceId;
                Verify(item.Plan, item.Instance, item.Symbol, request.Pin);
            }
            ToolRequestLifetime.Current?.Expiration.ThrowIfCancellationRequested();
            var output = new JObject
            {
                ["transactionState"] = "committed", ["dryRun"] = false, ["modelChanged"] = true,
                ["units"] = "Revit internal units; coordinates and lengths in feet",
                ["atomic"] = true, ["pinned"] = request.Pin,
                ["results"] = new JArray(created.Select(item => Receipt(item.Plan, item.Instance))),
                ["typesCreated"] = new JArray(createdTypes.Values.Select(s => new JObject
                {
                    ["family"] = s.FamilyName, ["typeName"] = s.Name, ["typeId"] = s.Id.Value
                }))
            };
            if (output.ToString(Formatting.None).Length > 256 * 1024)
                throw new InvalidOperationException("The response exceeds its budget; atomic changes will be rolled back.");
            if (group.Assimilate() != TransactionStatus.Committed)
                throw new InvalidOperationException("TransactionGroup did not assimilate successfully.");
            return CortexResult<object>.Ok(output);
        }
        catch (Exception ex)
        {
            // In particular, do not read elements after RegenerationFailedException.
            // Only transaction rollback/status operations are attempted here.
            var rollbackErrors = new List<string>();
            var transactionState = "none";
            var groupState = "none";
            try
            {
                if (txAttempted)
                {
                    var status = tx.GetStatus();
                    if (status == TransactionStatus.Started) status = tx.RollBack();
                    transactionState = status.ToString();
                }
            }
            catch (Exception rollback) { transactionState = "unknown"; rollbackErrors.Add(Bound(rollback.Message)); }
            try
            {
                if (groupAttempted)
                {
                    var status = group.GetStatus();
                    if (status == TransactionStatus.Started) status = group.RollBack();
                    groupState = status.ToString();
                }
            }
            catch (Exception rollback) { groupState = "unknown"; rollbackErrors.Add(Bound(rollback.Message)); }
            var rolledBack = groupState == TransactionStatus.RolledBack.ToString();
            var noChanges = !groupAttempted && !txAttempted;
            return CortexResult<object>.Fail(ex is OperationCanceledException ? CortexErrorCode.Cancelled : CortexErrorCode.TransactionFailed,
                rolledBack ? "Structural authoring failed; all changes from this batch were rolled back. " + Bound(ex.Message) :
                    noChanges ? "Structural authoring did not start. " + Bound(ex.Message) :
                    "Structural authoring failed; rollback is not confirmed. " + Bound(ex.Message),
                suggestion: "Inspect sourceId and primitive failure diagnostics. Verify model state before retrying; never blindly repeat an uncertain batch.",
                context: new Dictionary<string, object>
                {
                    ["sourceId"] = sourceId, ["transactionState"] = transactionState,
                    ["transactionGroupState"] = groupState, ["rollbackConfirmed"] = rolledBack,
                    ["modelChanges"] = rolledBack || noChanges ? "none" : "unknown",
                    ["createdElementIds"] = Array.Empty<long>(), ["rollbackErrors"] = rollbackErrors,
                    ["failures"] = failures?.Failures ?? Array.Empty<Dictionary<string, object>>(),
                    ["omittedFailures"] = failures?.OmittedFailures ?? 0,
                    ["diagnosticReportPath"] = failures?.DiagnosticReportPath ?? "(none)"
                });
        }
    }

    private static FamilyInstance Place(Document doc, PlanEntry plan, FamilySymbol symbol)
    {
        var m = plan.Spec;
        if (m.Role == "beam")
            return doc.Create.NewFamilyInstance(Line.CreateBound(Point(m.P0), Point(m.P1)), symbol, plan.BaseLevel, StructuralType.Beam)
                ?? throw new InvalidOperationException("Revit returned no beam instance.");
        var lower = m.P0[2] < m.P1[2] ? m.P0 : m.P1;
        var upper = m.P0[2] < m.P1[2] ? m.P1 : m.P0;
        var instance = doc.Create.NewFamilyInstance(Point(lower), symbol, plan.BaseLevel, StructuralType.Column)
            ?? throw new InvalidOperationException("Revit returned no column instance.");
        Set(instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM), new JValue(plan.BaseLevel.Id.Value));
        Set(instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM), new JValue(plan.TopLevel.Id.Value));
        Set(instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM), new JValue(lower[2] - plan.BaseLevel.ProjectElevation));
        Set(instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM), new JValue(upper[2] - plan.TopLevel.ProjectElevation));
        return instance;
    }

    private static void Verify(PlanEntry plan, FamilyInstance instance, FamilySymbol symbol, bool pin)
    {
        if (!instance.IsValidObject || instance.GetTypeId().Value != symbol.Id.Value || instance.Pinned != pin)
            throw new InvalidOperationException($"{plan.Spec.SourceId}: placed instance/type/pinning verification failed.");
        foreach (var p in plan.Spec.TypeParams.Properties()) VerifyParameter(symbol, p);
        foreach (var p in plan.Spec.InstanceParams.Properties()) VerifyParameter(instance, p);
        var m = plan.Spec;
        if (m.Role == "beam")
        {
            if (instance.Location is not LocationCurve location || location.Curve is not Line line ||
                !((Close(line.GetEndPoint(0), m.P0) && Close(line.GetEndPoint(1), m.P1)) ||
                  (Close(line.GetEndPoint(1), m.P0) && Close(line.GetEndPoint(0), m.P1))))
                throw new InvalidOperationException($"{m.SourceId}: actual beam axis differs from requested endpoints.");
        }
        else
        {
            var lower = m.P0[2] < m.P1[2] ? m.P0 : m.P1;
            var upper = m.P0[2] < m.P1[2] ? m.P1 : m.P0;
            if (instance.Location is not LocationPoint point ||
                Math.Abs(point.Point.X - lower[0]) > StructuralMemberInput.GeometryToleranceFt ||
                Math.Abs(point.Point.Y - lower[1]) > StructuralMemberInput.GeometryToleranceFt ||
                instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM)?.AsElementId().Value != plan.BaseLevel.Id.Value ||
                instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM)?.AsElementId().Value != plan.TopLevel.Id.Value ||
                !Matches(instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM), new JValue(lower[2] - plan.BaseLevel.ProjectElevation)) ||
                !Matches(instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM), new JValue(upper[2] - plan.TopLevel.ProjectElevation)))
                throw new InvalidOperationException($"{m.SourceId}: actual column position or level constraints differ from the request.");
        }
    }
    private static JObject Receipt(PlanEntry plan, FamilyInstance instance) => new()
    {
        ["sourceId"] = plan.Spec.SourceId, ["status"] = "created", ["elementId"] = instance.Id.Value,
        ["uniqueId"] = instance.UniqueId, ["family"] = plan.Spec.Family, ["typeName"] = plan.Spec.TypeName,
        ["placementVerified"] = true,
        ["verificationBasis"] = plan.Spec.Role == "beam" ? "actual LocationCurve line endpoints" : "LocationPoint XY and base/top level constraints; not solid extents",
        ["requestedP0_ft"] = new JArray(plan.Spec.P0), ["requestedP1_ft"] = new JArray(plan.Spec.P1)
    };
    private static void VerifyParameter(Element element, JProperty property)
    {
        if (!Matches(Parameter(element, property.Name), property.Value))
            throw new InvalidOperationException($"Parameter '{property.Name}' differs from its requested value after regeneration/commit.");
    }
    private static FamilySymbol? Unique(IEnumerable<FamilySymbol> source, string kind, StructuralMemberInput.Member member)
    {
        var found = source.Take(2).ToArray();
        if (found.Length > 1) throw new ArgumentException($"{member.SourceId}: ambiguous {kind}; family/type names must uniquely identify a symbol.");
        return found.FirstOrDefault();
    }
    private static Level LevelFor(Level[] levels, double z) => levels.LastOrDefault(l => l.ProjectElevation <= z + StructuralMemberInput.GeometryToleranceFt) ?? levels[0];
    private static Parameter Parameter(Element element, string name)
    {
        var found = element.GetParameters(name);
        if (found.Count != 1) throw new ArgumentException($"Parameter '{name}' must exist exactly once on the selected element/type.");
        return found[0];
    }
    private static void Apply(Element element, JObject values)
    {
        foreach (var p in values.Properties()) Set(Parameter(element, p.Name), p.Value);
    }
    private static void ValidateValue(Parameter p, JToken value)
    {
        var valid = p.StorageType switch
        {
            StorageType.Double => (value.Type == JTokenType.Float || value.Type == JTokenType.Integer) && double.IsFinite(value.Value<double>()),
            StorageType.Integer => value.Type == JTokenType.Integer && value.Value<double>() >= int.MinValue && value.Value<double>() <= int.MaxValue,
            StorageType.ElementId => value.Type == JTokenType.Integer && long.TryParse(value.ToString(Formatting.None), out _),
            StorageType.String => value.Type == JTokenType.String,
            _ => false
        };
        if (!valid) throw new ArgumentException($"Parameter '{p.Definition.Name}' requires a value compatible with {p.StorageType}; numeric values use Revit internal units.");
    }
    private static bool Matches(Parameter? p, JToken value)
    {
        if (p == null) return false;
        ValidateValue(p, value);
        return p.StorageType switch
        {
            StorageType.Double => Math.Abs(p.AsDouble() - value.Value<double>()) <= 1e-8 * Math.Max(1, Math.Abs(value.Value<double>())),
            StorageType.Integer => p.AsInteger() == value.Value<int>(),
            StorageType.ElementId => p.AsElementId().Value == value.Value<long>(),
            StorageType.String => (p.AsString() ?? "") == value.Value<string>(),
            _ => false
        };
    }
    private static void Set(Parameter? p, JToken value)
    {
        if (p == null) throw new ArgumentException("A required placement parameter is missing.");
        ValidateValue(p, value);
        if (Matches(p, value)) return;
        if (p.IsReadOnly) throw new ArgumentException($"Parameter '{p.Definition.Name}' is read-only.");
        var changed = p.StorageType switch
        {
            StorageType.Double => p.Set(value.Value<double>()), StorageType.Integer => p.Set(value.Value<int>()),
            StorageType.ElementId => p.Set(new ElementId(value.Value<long>())), StorageType.String => p.Set(value.Value<string>()!), _ => false
        };
        if (!changed || !Matches(p, value)) throw new InvalidOperationException($"Revit did not set parameter '{p.Definition.Name}' to the requested value.");
    }
    private static XYZ Point(double[] p) => new(p[0], p[1], p[2]);
    private static bool Close(XYZ point, double[] requested) => point.DistanceTo(Point(requested)) <= StructuralMemberInput.GeometryToleranceFt;
    private static string Bound(string text) => text.Length > 2048 ? text[..2048] : text;
    private sealed record PlanEntry(StructuralMemberInput.Member Spec, FamilySymbol Template, FamilySymbol? Target, Level BaseLevel, Level TopLevel);
}
