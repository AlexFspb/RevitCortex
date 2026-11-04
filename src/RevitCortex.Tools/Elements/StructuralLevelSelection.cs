using System;
using System.Collections.Generic;

namespace RevitCortex.Tools.Elements;

/// <summary>Level choice independent of native Revit objects. Offsets may legitimately be negative.</summary>
internal static class StructuralLevelSelection
{
    internal static int NamedIndex(IReadOnlyList<string> names, string requestedName, string sourceId)
    {
        var found = -1;
        for (var i = 0; i < names.Count; i++)
        {
            if (!string.Equals(names[i], requestedName, StringComparison.OrdinalIgnoreCase)) continue;
            if (found >= 0)
                throw new ArgumentException($"{sourceId}: level '{requestedName}' is ambiguous; use a name that uniquely identifies one level.");
            found = i;
        }
        return found >= 0 ? found : throw new ArgumentException($"{sourceId}: level '{requestedName}' was not found.");
    }

    internal static int AtOrBelowIndex(IReadOnlyList<double> elevations, double z)
    {
        if (elevations.Count == 0 || !double.IsFinite(z)) throw new ArgumentException("A finite elevation and at least one level are required.");
        var lowest = 0;
        var found = -1;
        for (var i = 0; i < elevations.Count; i++)
        {
            if (!double.IsFinite(elevations[i])) throw new ArgumentException("Level elevations must be finite.");
            if (elevations[i] < elevations[lowest]) lowest = i;
            if (elevations[i] <= z + StructuralMemberInput.GeometryToleranceFt &&
                (found < 0 || elevations[i] >= elevations[found])) found = i;
        }
        return found >= 0 ? found : lowest;
    }
}
