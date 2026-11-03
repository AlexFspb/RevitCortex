using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Read-only relationship query adapted from oakplank/RevitMCP (MIT),
/// lib/routes/element_routes.py at b00c193922212c34b50b2100a3461e526917a4a8.
/// No linked-document traversal, element mutation, or UI calls.
/// </summary>
[ToolSafety(true, false)]
public sealed class GetElementRelationshipsTool : ICortexTool
{
    public string Name => "get_element_relationships";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read explicit host, nesting, group, assembly, insert, dependent and adaptive-point relationships. Bounded output with field diagnostics; never resolves linked IDs in the host document.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        ElementRelationshipQuery query;
        try { query = ElementRelationshipQuery.Parse(input); }
        catch (ArgumentException ex) { return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, Clip(ex.Message, 512),
            suggestion: "Supply 1..100 positive elementIds; maxDepth is 1..16 and maxRelated is 1..100."); }
        var (doc, error) = ToolHelpers.RequireDocument(session);
        if (doc == null) return error!;
        var output = new ElementRelationshipResults();
        foreach (var id in query.ElementIds)
        {
            if (output.Truncated) { output.Add(id, null); continue; }
            JObject record;
            try
            {
                var element = doc.GetElement(new ElementId(id));
                record = element == null
                    ? new JObject { ["elementId"] = id, ["status"] = "missing", ["reason"] = "ElementNotFound" }
                    : Describe(doc, element, query);
            }
            catch (Exception ex) { record = new JObject { ["elementId"] = id, ["status"] = "error", ["error"] = Error(ex) }; }
            output.Add(id, record);
        }
        return CortexResult<object>.Ok(new JObject
        {
            ["requestedCount"] = query.ElementIds.Length, ["count"] = output.Records.Count,
            ["coordinatesUnit"] = "mm", ["coordinateSystem"] = "Revit internal model coordinates",
            ["relationships"] = output.Records, ["truncated"] = output.Truncated,
            ["retryNote"] = output.Truncated ? "Request not_read elementIds separately or reduce maxRelated. No requested ID was silently omitted." : null,
            ["scopeNote"] = "Current document only. Host chains follow direct host before super component. Dependents are Revit logical children, not all semantic or physical dependencies. Related-ID API collection itself may be larger than the bounded readout."
        });
    }

    private static JObject Describe(Document doc, Element element, ElementRelationshipQuery query)
    {
        var row = new JObject { ["elementId"] = element.Id.Value, ["status"] = "ok", ["element"] = Summary(element) };
        var fi = element as FamilyInstance;
        row["host"] = Relation(() => fi == null ? State("not_applicable") : Single(doc, fi.Host, "host"));
        row["superComponent"] = Relation(() => fi == null ? State("not_applicable") : Single(doc, fi.SuperComponent, "super_component"));
        row["hostReference"] = Relation(() => HostReference(fi));
        row["group"] = Relation(() => FromId(doc, element.GroupId, "group"));
        row["assembly"] = Relation(() => FromId(doc, element.AssemblyInstanceId, "assembly"));
        row["hostChain"] = query.IncludeHostChain ? Relation(() => HostChain(doc, element, query.MaxDepth)) : State("disabled");
        row["subComponents"] = query.IncludeSubComponents
            ? Relation(() => fi == null ? State("not_applicable") : DescribeIds(doc, fi.GetSubComponentIds(), "sub_component", query.MaxRelated))
            : State("disabled");
        row["hostedInserts"] = query.IncludeInserts
            ? Relation(() => element is HostObject host ? DescribeIds(doc, host.FindInserts(true, true, true, true), "hosted_insert", query.MaxRelated) : State("not_applicable"))
            : State("disabled");
        row["dependents"] = query.IncludeDependents
            ? Relation(() => DescribeIds(doc, element.GetDependentElements(null), "dependent", query.MaxRelated)) : State("disabled");
        row["adaptivePlacementPoints"] = query.IncludeAdaptivePoints
            ? Relation(() => AdaptivePoints(doc, fi, query.MaxRelated)) : State("disabled");
        if (row.DescendantsAndSelf().OfType<JObject>().Any(o => o.Value<string>("status") is "error" or "missing" || o["diagnostics"] is JArray a && a.Count > 0))
            row["status"] = "partial";
        return row;
    }

    private static JObject FromId(Document doc, ElementId id, string kind) => id.Value <= 0
        ? State("none") : Single(doc, doc.GetElement(id), kind, id.Value);

    private static JObject Single(Document doc, Element? element, string kind, long? missingId = null)
    {
        if (element == null)
            return missingId == null ? State("none") : new JObject { ["status"] = "missing", ["elementId"] = missingId, ["relationship"] = kind };
        if (!element.Document.Equals(doc))
            return new JObject { ["status"] = "link_boundary", ["relationship"] = kind, ["note"] = "The relation belongs to another document; its ID was not resolved in the host document." };
        return new JObject { ["status"] = "ok", ["relationship"] = kind, ["element"] = Summary(element),
            ["linkBoundary"] = element is RevitLinkInstance };
    }

    private static JObject HostReference(FamilyInstance? instance)
    {
        if (instance == null) return State("not_applicable");
        var reference = instance.HostFace;
        if (reference == null) return State("none");
        var linked = reference.LinkedElementId;
        return new JObject
        {
            ["status"] = "ok", ["hostDocumentElementId"] = reference.ElementId.Value,
            ["linkedElementId"] = linked.Value > 0 ? new JValue(linked.Value) : JValue.CreateNull(),
            ["linkBoundary"] = linked.Value > 0, ["linkedDocumentTraversed"] = false,
            ["note"] = linked.Value > 0 ? "linkedElementId belongs to the linked document, not the active document." : null
        };
    }

    private static JObject HostChain(Document doc, Element start, int maxDepth)
    {
        var chain = ElementRelationshipChain.Walk(start.Id.Value, maxDepth, id =>
        {
            var current = doc.GetElement(new ElementId(id)) ?? throw new InvalidOperationException("A host-chain element is no longer present.");
            if (current is not FamilyInstance instance) return null;
            var parent = instance.Host;
            var kind = "host";
            if (parent == null) { parent = instance.SuperComponent; kind = "super_component"; }
            if (parent == null) return null;
            if (!parent.Document.Equals(doc)) throw new InvalidOperationException("Host-chain traversal reached another document and stopped.");
            return new ElementRelationshipEdge(parent.Id.Value, kind, parent is RevitLinkInstance);
        });
        var entries = new JArray(chain.Edges.Select(edge => Single(doc, doc.GetElement(new ElementId(edge.ElementId)), edge.Kind, edge.ElementId)));
        return new JObject { ["status"] = entries.Count == 0 ? "none" : "ok", ["elements"] = entries,
            ["termination"] = chain.Termination, ["truncated"] = chain.Truncated, ["cycleDetected"] = chain.Termination == "cycle" };
    }

    private static JObject DescribeIds(Document doc, ICollection<ElementId> ids, string kind, int maxRelated)
    {
        var values = new JArray();
        foreach (var id in ids.Take(maxRelated))
        {
            try { values.Add(Single(doc, doc.GetElement(id), kind, id.Value)); }
            catch (Exception ex) { values.Add(new JObject { ["elementId"] = id.Value, ["status"] = "error", ["error"] = Error(ex) }); }
        }
        return new JObject { ["status"] = ids.Count == 0 ? "none" : "ok", ["elements"] = values,
            ["totalCount"] = ids.Count, ["returnedCount"] = values.Count, ["truncated"] = ids.Count > values.Count };
    }

    private static JObject AdaptivePoints(Document doc, FamilyInstance? instance, int maxRelated)
    {
        if (instance == null || !AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(instance)) return State("not_applicable");
        var ids = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance);
        var points = new JArray();
        var index = 0;
        foreach (var id in ids.Take(maxRelated))
        {
            var value = new JObject { ["index"] = index++, ["pointElementId"] = id.Value };
            try
            {
                if (doc.GetElement(id) is not ReferencePoint point) throw new InvalidOperationException("Adaptive placement point is missing or not a ReferencePoint.");
                var p = point.Position;
                var mm = new[] { p.X * 304.8, p.Y * 304.8, p.Z * 304.8 };
                if (mm.Any(v => !double.IsFinite(v))) throw new InvalidOperationException("Adaptive point position contains a non-finite coordinate.");
                value["status"] = "ok"; value["position_mm"] = new JArray(mm); value["element"] = Summary(point);
            }
            catch (Exception ex) { value["status"] = "error"; value["error"] = Error(ex); }
            points.Add(value);
        }
        return new JObject { ["status"] = ids.Count == 0 ? "none" : "ok", ["points"] = points,
            ["totalCount"] = ids.Count, ["returnedCount"] = points.Count, ["truncated"] = ids.Count > points.Count };
    }

    private static JObject Summary(Element element)
    {
        var diagnostics = new JArray();
        var summary = new JObject { ["elementId"] = element.Id.Value };
        Field(summary, "uniqueId", () => element.UniqueId, diagnostics);
        Field(summary, "name", () => element.Name, diagnostics);
        Field(summary, "category", () => element.Category?.Name, diagnostics);
        Field(summary, "familyName", () => element is FamilyInstance fi ? fi.Symbol?.FamilyName : null, diagnostics);
        Field(summary, "typeId", () => ValidId(element.GetTypeId()), diagnostics);
        Field(summary, "ownerViewId", () => ValidId(element.OwnerViewId), diagnostics);
        if (diagnostics.Count > 0) summary["diagnostics"] = diagnostics;
        return summary;
    }

    private static object? ValidId(ElementId id) => id.Value > 0 ? id.Value : null;
    private static void Field(JObject output, string name, Func<object?> read, JArray diagnostics)
    {
        try
        {
            var value = read();
            output[name] = value == null ? JValue.CreateNull() : value is string text ? new JValue(Clip(text, 128)) : JToken.FromObject(value);
        }
        catch (Exception ex) { output[name] = JValue.CreateNull(); diagnostics.Add(new JObject { ["field"] = name, ["error"] = Error(ex) }); }
    }
    private static JObject Relation(Func<JObject> read)
    {
        try { return read(); }
        catch (Exception ex) { return new JObject { ["status"] = "error", ["error"] = Error(ex) }; }
    }
    private static JObject State(string status) => new() { ["status"] = status };
    private static JObject Error(Exception ex) => new() { ["code"] = "RelationshipReadFailed", ["message"] = Clip(ex.Message, 512) };
    private static string Clip(string text, int size) => text.Length > size ? text[..size] + "…" : text;
}
