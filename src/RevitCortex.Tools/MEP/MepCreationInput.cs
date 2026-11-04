using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.MEP;

/// <summary>Primitive input validation and explicit mm conversion, without native Revit calls.</summary>
internal sealed record MepCreationInput(bool IsPipe, long SystemTypeId, long TypeId, long LevelId,
    double[] StartMm, double[] EndMm, string Shape, double? DiameterMm, double? WidthMm, double? HeightMm, bool DryRun)
{
    internal const double MmPerFoot = 304.8;
    internal const double MinimumLengthMm = 2.54; // MEPCurve point creation: 1/10 inch.
    internal const double GeometryToleranceFt = 1e-6;
    internal static double Feet(double mm) => mm / MmPerFoot;
    internal static double Millimeters(double feet) => feet * MmPerFoot;
    internal double LengthMm => Math.Sqrt(StartMm.Zip(EndMm, (a, b) => (a - b) * (a - b)).Sum());

    internal static MepCreationInput Parse(JObject input, bool pipe)
    {
        if (input.ToString(Newtonsoft.Json.Formatting.None).Length > 16 * 1024)
            throw new ArgumentException("The native MEP request exceeds 16 Ki characters.");
        var allowed = new HashSet<string>(new[] { "systemTypeId", pipe ? "pipeTypeId" : "ductTypeId", "levelId",
            "startPoint", "endPoint", "diameterMm", "dryRun", "_cortexExpected" }, StringComparer.Ordinal);
        if (!pipe) allowed.UnionWith(new[] { "shape", "widthMm", "heightMm" });
        var unknown = input.Properties().FirstOrDefault(p => !allowed.Contains(p.Name));
        if (unknown != null) throw new ArgumentException($"Unsupported field '{unknown.Name}'.");
        var start = Point(input, "startPoint"); var end = Point(input, "endPoint");
        var shape = pipe ? "round" : input["shape"]?.Type == JTokenType.String
            ? input["shape"]!.Value<string>()!.Trim().ToLowerInvariant() : "";
        if (shape != "round" && shape != "rectangular" && shape != "oval")
            throw new ArgumentException("shape must be round, rectangular or oval.");
        var diameter = Size(input, "diameterMm");
        var width = pipe ? null : Size(input, "widthMm");
        var height = pipe ? null : Size(input, "heightMm");
        if (shape == "round" && (diameter == null || width != null || height != null))
            throw new ArgumentException("Round pipes/ducts require diameterMm only; widthMm and heightMm are not applicable.");
        if (shape != "round" && (diameter != null || width == null || height == null))
            throw new ArgumentException("Rectangular/oval ducts require widthMm and heightMm only; diameterMm is not applicable.");
        var dryRun = input["dryRun"] == null ? true : input["dryRun"]!.Type == JTokenType.Boolean
            ? input["dryRun"]!.Value<bool>() : throw new ArgumentException("dryRun must be boolean.");
        var result = new MepCreationInput(pipe, Id(input, "systemTypeId"), Id(input, pipe ? "pipeTypeId" : "ductTypeId"),
            Id(input, "levelId"), start, end, shape, diameter, width, height, dryRun);
        if (!double.IsFinite(result.LengthMm) || result.LengthMm < MinimumLengthMm)
            throw new ArgumentException("startPoint/endPoint must define a straight segment at least 2.54 mm long.");
        return result;
    }

    private static long Id(JObject input, string name)
    {
        var value = input[name];
        if (value?.Type != JTokenType.Integer || !long.TryParse(value.ToString(), out var id) || id <= 0)
            throw new ArgumentException($"{name} must be a positive 64-bit element ID in the active project.");
        return id;
    }
    private static double[] Point(JObject input, string name)
    {
        if (input[name] is not JArray point || point.Count != 3)
            throw new ArgumentException($"{name} must be [x,y,z] in millimeters relative to the Revit internal origin.");
        var values = point.Select(v => Number(v, name)).ToArray();
        if (values.Any(v => Math.Abs(v) > 1e9)) throw new ArgumentException($"{name} exceeds the coordinate input limit.");
        return values;
    }
    private static double Number(JToken value, string name)
    {
        if (value.Type != JTokenType.Float && value.Type != JTokenType.Integer)
            throw new ArgumentException($"{name} requires finite numeric values, not text or booleans.");
        if (value.Type == JTokenType.Integer && !long.TryParse(value.ToString(Newtonsoft.Json.Formatting.None),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException($"{name}: integer values must fit the signed 64-bit range.");
        var parsed = value.Value<double>();
        if (!double.IsFinite(parsed)) throw new ArgumentException($"{name} requires finite numeric values.");
        return parsed;
    }
    private static double? Size(JObject input, string name)
    {
        if (input[name] == null || input[name]!.Type == JTokenType.Null) return null;
        var value = Number(input[name]!, name);
        if (value <= 0 || value > 1e6) throw new ArgumentException($"{name} must be greater than zero and at most 1000000 mm.");
        return value;
    }
}
