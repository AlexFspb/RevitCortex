using System;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Elements;

/// <summary>Primitive constraint readout. Computed elevations are not an independent geometry measurement.</summary>
internal static class StructuralColumnConstraintData
{
    internal static JObject Create(long baseLevelId, long topLevelId, double baseElevation, double topElevation,
        double baseOffset, double topOffset)
    {
        var baseZ = baseElevation + baseOffset;
        var topZ = topElevation + topOffset;
        if (baseLevelId <= 0 || topLevelId <= 0 || !double.IsFinite(baseElevation) || !double.IsFinite(topElevation) ||
            !double.IsFinite(baseOffset) || !double.IsFinite(topOffset) || !double.IsFinite(baseZ) || !double.IsFinite(topZ))
            throw new InvalidOperationException("Column level constraints contain invalid IDs or non-finite elevations.");
        return new JObject
        {
            ["status"] = "ok", ["basis"] = "constraint-derived, not physical geometry",
            ["physicalZVerified"] = false,
            ["baseLevelId"] = baseLevelId, ["topLevelId"] = topLevelId,
            ["baseLevelProjectElevation_ft"] = baseElevation, ["topLevelProjectElevation_ft"] = topElevation,
            ["baseOffset_ft"] = baseOffset, ["topOffset_ft"] = topOffset,
            ["baseZ_ft"] = baseZ, ["topZ_ft"] = topZ
        };
    }

    internal static void ApplyVerification(JObject receipt, bool column)
    {
        receipt["placementVerified"] = !column;
        receipt["verificationBasis"] = column
            ? "LocationPoint XY and base/top level constraints; physical Z and solid extents not verified"
            : "actual LocationCurve line endpoints";
        if (column)
        {
            receipt["constraintsVerified"] = true;
            receipt["physicalZVerified"] = false;
        }
    }
}
