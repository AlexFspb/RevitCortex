using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Elements;

/// <summary>Pure input and geometry contract; does not load the native Revit API.</summary>
internal sealed class StructuralLocationQuery
{
    internal const int MaxRequestedIds = 5000;
    internal const int MaxPageSize = 500;
    internal const int ResponseBudgetBytes = 1_000_000;
    internal string[] Categories { get; private set; } = new[] { "StructuralFraming", "StructuralColumns" };
    internal long[]? ElementIds { get; private set; }
    internal bool IncludeTypeParams { get; private set; }
    internal int Offset { get; private set; }
    internal int Limit { get; private set; } = MaxPageSize;

    internal static bool TryParse(JObject input, out StructuralLocationQuery query, out string? error)
    {
        query = new StructuralLocationQuery();
        error = null;
        if (input["categories"] is JToken categories && categories.Type != JTokenType.Null)
        {
            if (categories is not JArray list || list.Count == 0 || list.Count > 2)
            { error = "categories must contain one or two names: StructuralFraming, StructuralColumns"; return false; }
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in list)
            {
                if (item.Type != JTokenType.String ||
                    !query.Categories.Any(c => string.Equals(c, (string?)item, StringComparison.OrdinalIgnoreCase)))
                { error = "categories supports only StructuralFraming and StructuralColumns"; return false; }
                selected.Add((string)item!);
            }
            query.Categories = query.Categories.Where(selected.Contains).ToArray();
        }
        if (input["elementIds"] is JToken ids && ids.Type != JTokenType.Null)
        {
            if (ids is not JArray list || list.Count > MaxRequestedIds)
            { error = $"elementIds must be an array of at most {MaxRequestedIds} positive integers"; return false; }
            var parsed = new SortedSet<long>();
            foreach (var id in list)
            {
                if (id.Type != JTokenType.Integer || !long.TryParse(id.ToString(), out var value) || value <= 0)
                { error = "elementIds must contain positive 64-bit integers"; return false; }
                parsed.Add(value);
            }
            query.ElementIds = parsed.ToArray();
        }
        if (!ReadInt(input, "offset", 0, int.MaxValue, 0, out var offset) ||
            !ReadInt(input, "limit", 1, MaxPageSize, MaxPageSize, out var limit))
        { error = $"offset must be a nonnegative integer; limit must be 1..{MaxPageSize}"; return false; }
        query.Offset = offset;
        query.Limit = limit;
        if (input["includeTypeParams"] is JToken include && include.Type != JTokenType.Null)
        {
            if (include.Type != JTokenType.Boolean)
            { error = "includeTypeParams must be a boolean"; return false; }
            query.IncludeTypeParams = (bool)include;
        }
        return true;
    }

    private static bool ReadInt(JObject input, string key, int min, int max, int fallback, out int value)
    {
        value = fallback;
        var token = input[key];
        if (token == null || token.Type == JTokenType.Null) return true;
        return token.Type == JTokenType.Integer && int.TryParse(token.ToString(), out value) && value >= min && value <= max;
    }
}

internal static class StructuralLocationData
{
    internal static JObject Line(double[] p0, double[] p1) => new()
    {
        ["curveType"] = "line", ["axisIsExact"] = true, ["canReconstruct"] = true,
        ["p0_ft"] = new JArray(p0), ["p1_ft"] = new JArray(p1)
    };

    internal static JObject Arc(double[] p0, double[] p1, double[] midpoint, double[] center, double radius, double[] normal) => new()
    {
        ["curveType"] = "arc", ["axisIsExact"] = true, ["canReconstruct"] = true,
        ["p0_ft"] = new JArray(p0), ["p1_ft"] = new JArray(p1),
        ["midpoint_ft"] = new JArray(midpoint), ["center_ft"] = new JArray(center),
        ["radius_ft"] = radius, ["normal"] = new JArray(normal)
    };

    internal static JObject Point(double[] point) => new()
    {
        ["curveType"] = "point", ["axisIsExact"] = false, ["canReconstruct"] = false,
        ["point_ft"] = new JArray(point),
        ["note"] = "This is an insertion point, not a measured column axis. No axis was inferred from a bounding box."
    };

    internal static JObject Unsupported(string kind) => new()
    {
        ["curveType"] = kind, ["axisIsExact"] = false, ["canReconstruct"] = false,
        ["note"] = "No supported bounded location curve is available. No axis was inferred from geometry extents."
    };
}
