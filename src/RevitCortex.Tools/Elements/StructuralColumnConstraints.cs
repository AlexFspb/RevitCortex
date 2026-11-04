using System;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Elements;

internal static class StructuralColumnConstraints
{
    /// <summary>Read actual level IDs/offsets. Callers decide whether a read error fails a write or becomes a query diagnostic.</summary>
    internal static JObject Read(Document doc, Element column)
    {
        if (!column.IsValidObject || !column.Document.Equals(doc))
            throw new InvalidOperationException("The column is not valid in the current document.");
        var baseId = Id(column, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
        var topId = Id(column, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
        var baseLevel = doc.GetElement(baseId) as Level
            ?? throw new InvalidOperationException("The column base constraint does not identify a Level.");
        var topLevel = doc.GetElement(topId) as Level
            ?? throw new InvalidOperationException("The column top constraint does not identify a Level.");
        return StructuralColumnConstraintData.Create(baseId.Value, topId.Value, baseLevel.ProjectElevation, topLevel.ProjectElevation,
            Offset(column, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM), Offset(column, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM));
    }

    private static ElementId Id(Element column, BuiltInParameter id)
    {
        var parameter = column.get_Parameter(id);
        if (parameter?.StorageType != StorageType.ElementId)
            throw new InvalidOperationException($"Required column level parameter {id} is unavailable.");
        var value = parameter.AsElementId();
        if (value == null || value.Value <= 0) throw new InvalidOperationException($"Column level parameter {id} has no valid level ID.");
        return value;
    }

    private static double Offset(Element column, BuiltInParameter id)
    {
        var parameter = column.get_Parameter(id);
        if (parameter?.StorageType != StorageType.Double)
            throw new InvalidOperationException($"Required column offset parameter {id} is unavailable.");
        return parameter.AsDouble();
    }
}
