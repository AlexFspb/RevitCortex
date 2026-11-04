using System;
using System.Globalization;

namespace RevitCortex.Tools.MEP;

/// <summary>Primitive size diagnostics. Actual native values are supplied before rollback handling.</summary>
internal static class MepSizeDiagnostic
{
    internal static string Message(string parameterId, double requestedMm, double? actualFeet, string reason)
    {
        var actualMm = actualFeet.HasValue ? MepCreationInput.Millimeters(actualFeet.Value) : (double?)null;
        var actual = actualMm.HasValue && double.IsFinite(actualMm.Value)
            ? Number(actualMm.Value) : "unavailable";
        return $"Size parameter {parameterId}: {reason}; requestedMm={Number(requestedMm)}; actualMm={actual}.";
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
