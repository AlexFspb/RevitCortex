using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Elements;

/// <summary>Primitive structural request validation. No native Revit calls.</summary>
internal sealed class StructuralMemberInput
{
    internal const int MaxMembers = 100;
    internal const int MaxParameterCount = 32;
    internal const double GeometryToleranceFt = 1e-6;
    internal bool DryRun { get; private init; }
    internal bool Pin { get; private init; }
    internal IReadOnlyList<Member> Members { get; private init; } = Array.Empty<Member>();

    internal static StructuralMemberInput Parse(JObject input)
    {
        if (input.ToString(Formatting.None).Length > 256 * 1024)
            throw new ArgumentException("The structural request exceeds 256 Ki characters.");
        RejectUnknown(input, "members", "dryRun", "pin", "_cortexExpected");
        if (input["members"] is not JArray array || array.Count < 1 || array.Count > MaxMembers)
            throw new ArgumentException($"members must contain 1–{MaxMembers} entries.");
        var members = new List<Member>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var types = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in array)
        {
            if (token is not JObject item) throw new ArgumentException("Every members entry must be an object.");
            RejectUnknown(item, "sourceId", "role", "family", "baseType", "typeName", "p0_ft", "p1_ft", "typeParams", "instanceParams", "levelName");
            var member = new Member(Text(item, "sourceId"), Text(item, "role").ToLowerInvariant(),
                Text(item, "family"), Text(item, "baseType"), Text(item, "typeName"),
                Point(item, "p0_ft"), Point(item, "p1_ft"), Parameters(item, "typeParams"),
                Parameters(item, "instanceParams"), OptionalText(item, "levelName"));
            if (!ids.Add(member.SourceId)) throw new ArgumentException($"Duplicate sourceId '{member.SourceId}'.");
            if (member.Role != "beam" && member.Role != "column") throw new ArgumentException("role must be beam or column.");
            if (member.Role == "column" && (Math.Abs(member.P0[0] - member.P1[0]) > GeometryToleranceFt ||
                Math.Abs(member.P0[1] - member.P1[1]) > GeometryToleranceFt))
                throw new ArgumentException($"{member.SourceId}: only vertical columns are supported; upper and lower X/Y must match.");
            if (Distance(member.P0, member.P1) <= GeometryToleranceFt)
                throw new ArgumentException($"{member.SourceId}: endpoints must be distinct.");
            if (types.TryGetValue(member.TypeKey, out var previous) &&
                (previous.Role != member.Role || !previous.BaseType.Equals(member.BaseType, StringComparison.OrdinalIgnoreCase) || !SameParameters(previous.TypeParams, member.TypeParams)))
                throw new ArgumentException($"{member.SourceId}: requested family/type has conflicting base type, role or typeParams in this batch.");
            types[member.TypeKey] = member;
            members.Add(member);
        }
        return new StructuralMemberInput { DryRun = Flag(input, "dryRun", true), Pin = Flag(input, "pin", false), Members = members };
    }

    internal static double Distance(double[] a, double[] b) => Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());
    internal static bool SameParameters(JObject a, JObject b) => a.Count == b.Count && a.Properties().All(p =>
        b.TryGetValue(p.Name, StringComparison.Ordinal, out var other) && SameValue(p.Value, other));
    private static bool SameValue(JToken a, JToken b)
    {
        if (a.Type == JTokenType.Integer && b.Type == JTokenType.Integer)
            return JToken.DeepEquals(a, b); // Element IDs above 2^53 must never pass through double.
        if (!IsNumber(a) || !IsNumber(b)) return JToken.DeepEquals(a, b);
        if ((a.Type == JTokenType.Integer && Math.Abs(a.Value<double>()) > 9007199254740991d) ||
            (b.Type == JTokenType.Integer && Math.Abs(b.Value<double>()) > 9007199254740991d)) return false;
        return a.Value<double>() == b.Value<double>();
    }
    private static bool IsNumber(JToken value) => value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
    private static bool Flag(JObject input, string name, bool fallback) => input[name] == null ? fallback :
        input[name]!.Type == JTokenType.Boolean ? input[name]!.Value<bool>() : throw new ArgumentException($"{name} must be boolean.");
    private static string Text(JObject item, string name)
    {
        var text = item[name]?.Type == JTokenType.String ? item[name]!.Value<string>()?.Trim() : null;
        if (string.IsNullOrEmpty(text) || text.Length > 256 || text.Any(char.IsControl))
            throw new ArgumentException($"{name} must be a nonempty string of up to 256 characters without control characters.");
        return text;
    }
    private static string? OptionalText(JObject item, string name) => item[name] == null || item[name]!.Type == JTokenType.Null ? null : Text(item, name);
    private static double[] Point(JObject item, string name)
    {
        if (item[name] is not JArray point || point.Count != 3 || point.Any(v => !IsNumber(v)))
            throw new ArgumentException($"{name} must contain three finite numbers in Revit internal feet.");
        var values = point.Select(v => FiniteNumber(v, name)).ToArray();
        if (values.Any(v => !double.IsFinite(v) || Math.Abs(v) > 1e7))
            throw new ArgumentException($"{name} contains an invalid or out-of-range coordinate.");
        return values;
    }
    private static JObject Parameters(JObject item, string name)
    {
        if (item[name] == null || item[name]!.Type == JTokenType.Null) return new JObject();
        if (item[name] is not JObject parameters || parameters.Count > MaxParameterCount)
            throw new ArgumentException($"{name} must be an object with at most {MaxParameterCount} parameter values.");
        foreach (var p in parameters.Properties())
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 256 || p.Name.Any(char.IsControl))
                throw new ArgumentException($"{name} contains an invalid parameter name.");
            if (p.Value.Type == JTokenType.String)
            {
                if (p.Value.Value<string>()!.Length > 1024) throw new ArgumentException($"{p.Name}: parameter text exceeds 1024 characters.");
            }
            else
                _ = FiniteNumber(p.Value, p.Name);
        }
        return (JObject)parameters.DeepClone();
    }
    private static double FiniteNumber(JToken token, string name)
    {
        if (!IsNumber(token))
            throw new ArgumentException($"{name}: use a finite number or string; use integer 0/1 for yes/no parameters.");
        // Newtonsoft represents larger integers as BigInteger. Reject them as input
        // errors before Value<double>() can throw or an ElementId can lose precision.
        if (token.Type == JTokenType.Integer && !long.TryParse(token.ToString(Formatting.None),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException($"{name}: integer values must fit the signed 64-bit range.");
        var value = token.Value<double>();
        if (!double.IsFinite(value)) throw new ArgumentException($"{name}: numeric values must be finite.");
        return value;
    }
    private static void RejectUnknown(JObject input, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.Ordinal);
        var unknown = input.Properties().FirstOrDefault(p => !names.Contains(p.Name));
        if (unknown != null) throw new ArgumentException($"Unsupported field '{unknown.Name}'.");
    }

    internal sealed record Member(string SourceId, string Role, string Family, string BaseType, string TypeName,
        double[] P0, double[] P1, JObject TypeParams, JObject InstanceParams, string? LevelName)
    {
        internal string TypeKey => Family + "\u001f" + TypeName;
    }
}
