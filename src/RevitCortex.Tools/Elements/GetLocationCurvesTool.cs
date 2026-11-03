using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Utilities;

namespace RevitCortex.Tools.Elements;

/// <summary>Bounded read-only structural geometry query, adapted from woodhouse711/RevitCortex.</summary>
[ToolSafety(true, false)]
public sealed class GetLocationCurvesTool : ICortexTool
{
    public string Name => "get_location_curves";
    public string Category => "Elements";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "Read actual location curves or insertion points of beams and columns, with bounded pagination. Coordinates are internal feet; a point or bounding box is never reported as an exact axis.";
    private const int MaxParametersInspected = 256;
    private const int MaxParametersReturned = 64;

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        if (!StructuralLocationQuery.TryParse(input, out var query, out var inputError))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, inputError!,
                suggestion: "Use StructuralFraming/StructuralColumns, positive element IDs, offset >= 0, and limit 1..500.");
        var (doc, documentError) = ToolHelpers.RequireDocument(session);
        if (doc == null) return documentError!;
        try
        {
            var categories = query.Categories.Select(c => c == "StructuralFraming"
                ? BuiltInCategory.OST_StructuralFraming : BuiltInCategory.OST_StructuralColumns).ToArray();
            // Explicit IDs use direct lookup. Discovery reads IDs only before pagination, not all element properties.
            var ids = query.ElementIds ?? new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(categories)).WhereElementIsNotElementType()
                .ToElementIds().Select(id => id.Value).OrderBy(id => id).ToArray();
            var elements = new JArray();
            var diagnostics = new JArray();
            var typeCache = new Dictionary<long, JObject>();
            var usedBytes = 16_384; // Reserve room for pagination and diagnostic metadata.
            var processed = 0;
            foreach (var id in ids.Skip(query.Offset).Take(query.Limit))
            {
                JObject? row = null;
                JObject? diagnostic = null;
                try
                {
                    var element = doc.GetElement(new ElementId(id));
                    if (element == null)
                        diagnostic = Diagnostic(id, "ElementNotFound", "The element does not exist in the active document.");
                    else if (element is ElementType || !categories.Any(c => (long)c == element.Category?.Id.Value))
                        diagnostic = Diagnostic(id, "CategoryMismatch", "The element is not an instance in the requested structural categories.");
                    else
                        row = Describe(doc, element, query.IncludeTypeParams, typeCache);
                }
                catch (Exception ex)
                { diagnostic = Diagnostic(id, "ElementReadFailed", ex.Message); }
                var bytes = Encoding.UTF8.GetByteCount((row ?? diagnostic!).ToString(Formatting.None));
                if (usedBytes + bytes > StructuralLocationQuery.ResponseBudgetBytes) break;
                if (row != null) elements.Add(row); else diagnostics.Add(diagnostic!);
                usedBytes += bytes;
                processed++;
            }
            var next = (long)query.Offset + processed;
            var hasMore = next < ids.Length;
            return CortexResult<object>.Ok(new JObject
            {
                ["coordinatesUnit"] = "ft", ["coordinateSystem"] = "Revit internal model coordinates",
                ["typeParameterValuesUnit"] = "Revit internal units; see dataType on each parameter. Not all values are lengths.",
                ["totalCount"] = ids.Length, ["offset"] = query.Offset, ["processedCount"] = processed,
                ["count"] = elements.Count, ["nextOffset"] = hasMore ? JToken.FromObject(next) : JValue.CreateNull(),
                ["truncated"] = hasMore, ["elements"] = elements, ["diagnostics"] = diagnostics,
                ["paginationNote"] = query.ElementIds == null
                    ? "Offsets refer to sorted matching IDs. Revalidate the document and restart pagination if the model changes."
                    : "Offsets refer to sorted unique requested IDs, including missing and excluded IDs."
            });
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(CortexErrorCode.Unknown, "Could not read structural locations: " + Clip(ex.Message),
                suggestion: "Verify the active document. Reduce the request to specific elementIds before retrying.");
        }
    }

    private static JObject Describe(Document doc, Element element, bool includeTypeParams, Dictionary<long, JObject> typeCache)
    {
        var diagnostics = new JArray();
        var row = new JObject { ["elementId"] = element.Id.Value };
        Read(row, "uniqueId", () => element.UniqueId, diagnostics);
        Read(row, "category", () => element.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming
            ? "StructuralFraming" : "StructuralColumns", diagnostics);
        var type = doc.GetElement(element.GetTypeId());
        Read(row, "familyName", () => (type as FamilySymbol)?.FamilyName, diagnostics);
        Read(row, "typeName", () => type?.Name, diagnostics);
        Read(row, "mark", () => element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(), diagnostics);
        Read(row, "levelName", () => FindLevelName(doc, element), diagnostics);
        try
        {
            var location = element.Location;
            JObject geometry;
            if (location is LocationCurve lc && lc.Curve is Curve curve)
            {
                if (!curve.IsBound) geometry = StructuralLocationData.Unsupported("unbound");
                else if (curve is Line)
                    geometry = StructuralLocationData.Line(Coordinates(curve.GetEndPoint(0)), Coordinates(curve.GetEndPoint(1)));
                else if (curve is Arc arc)
                    geometry = StructuralLocationData.Arc(Coordinates(arc.GetEndPoint(0)), Coordinates(arc.GetEndPoint(1)),
                        Coordinates(arc.Evaluate(0.5, true)), Coordinates(arc.Center), arc.Radius, Coordinates(arc.Normal));
                else geometry = StructuralLocationData.Unsupported(curve.GetType().Name);
            }
            else if (location is LocationPoint lp) geometry = StructuralLocationData.Point(Coordinates(lp.Point));
            else geometry = StructuralLocationData.Unsupported("none");
            foreach (var property in geometry.Properties()) row[property.Name] = property.Value;
        }
        catch (Exception ex)
        {
            foreach (var property in StructuralLocationData.Unsupported("unavailable").Properties()) row[property.Name] = property.Value;
            diagnostics.Add(new JObject { ["code"] = "LocationReadFailed", ["message"] = Clip(ex.Message) });
        }
        if (includeTypeParams && type != null)
        {
            if (!typeCache.TryGetValue(type.Id.Value, out var parameters))
                typeCache[type.Id.Value] = parameters = ReadTypeParameters(type);
            row["typeParameters"] = parameters.DeepClone();
        }
        if (diagnostics.Count > 0) row["diagnostics"] = diagnostics;
        return row;
    }

    private static JObject ReadTypeParameters(Element type)
    {
        var values = new JArray();
        var diagnostics = new JArray();
        var inspected = 0;
        var truncated = false;
        try
        {
            foreach (Parameter parameter in type.Parameters)
            {
                if (++inspected > MaxParametersInspected || values.Count >= MaxParametersReturned) { truncated = true; break; }
                try
                {
                    if (parameter.StorageType != StorageType.Double) continue;
                    var value = parameter.AsDouble();
                    if (!double.IsFinite(value)) throw new InvalidOperationException("Non-finite parameter value.");
                    values.Add(new JObject
                    {
                        ["parameterId"] = parameter.Id.Value, ["name"] = Clip(parameter.Definition.Name),
                        ["value"] = value, ["dataType"] = Clip(parameter.Definition.GetDataType().TypeId)
                    });
                }
                catch (Exception ex)
                {
                    if (diagnostics.Count < 8) diagnostics.Add(new JObject { ["code"] = "ParameterReadFailed", ["message"] = Clip(ex.Message) });
                }
            }
        }
        catch (Exception ex)
        { diagnostics.Add(new JObject { ["code"] = "ParameterEnumerationFailed", ["message"] = Clip(ex.Message) }); }
        return new JObject { ["values"] = values, ["truncated"] = truncated, ["diagnostics"] = diagnostics };
    }

    private static string? FindLevelName(Document doc, Element element)
    {
        if (doc.GetElement(element.LevelId) is Level level) return level.Name;
        foreach (var id in new[] { BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM, BuiltInParameter.SCHEDULE_LEVEL_PARAM })
        {
            var parameter = element.get_Parameter(id);
            if (parameter?.StorageType == StorageType.ElementId && doc.GetElement(parameter.AsElementId()) is Level fallback)
                return fallback.Name;
        }
        return null;
    }

    private static void Read(JObject output, string field, Func<string?> read, JArray diagnostics)
    {
        try { var value = read(); output[field] = value == null ? JValue.CreateNull() : JValue.CreateString(Clip(value)); }
        catch (Exception ex) { diagnostics.Add(new JObject { ["code"] = "FieldReadFailed", ["field"] = field, ["message"] = Clip(ex.Message) }); }
    }

    private static JObject Diagnostic(long id, string code, string message) => new()
        { ["elementId"] = id, ["code"] = code, ["message"] = Clip(message) };
    private static string Clip(string text) => text.Length <= 512 ? text : text.Substring(0, 512) + "…";
    private static double[] Coordinates(XYZ point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
            throw new InvalidOperationException("Location contains a non-finite coordinate.");
        return new[] { point.X, point.Y, point.Z };
    }
}
