using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Tools.Elements;

/// <summary>Primitive request, traversal and response limits; no native Revit dependency.</summary>
internal sealed class ElementRelationshipQuery
{
    internal const int MaxRequestedIds = 100;
    internal long[] ElementIds { get; private init; } = Array.Empty<long>();
    internal bool IncludeHostChain { get; private init; }
    internal bool IncludeSubComponents { get; private init; }
    internal bool IncludeInserts { get; private init; }
    internal bool IncludeDependents { get; private init; }
    internal bool IncludeAdaptivePoints { get; private init; }
    internal int MaxDepth { get; private init; }
    internal int MaxRelated { get; private init; }

    internal static ElementRelationshipQuery Parse(JObject input)
    {
        var allowed = new HashSet<string>(new[] { "elementIds", "includeHostChain", "maxDepth", "includeSubComponents",
            "includeInserts", "includeDependents", "includeAdaptivePoints", "maxRelated", "_cortexExpected" }, StringComparer.Ordinal);
        var unknown = input.Properties().FirstOrDefault(p => !allowed.Contains(p.Name));
        if (unknown != null) throw new ArgumentException($"Unsupported field '{unknown.Name}'.");
        if (input["elementIds"] is not JArray ids || ids.Count < 1 || ids.Count > MaxRequestedIds)
            throw new ArgumentException($"elementIds must contain 1..{MaxRequestedIds} positive 64-bit integers.");
        var unique = new List<long>();
        var seen = new HashSet<long>();
        foreach (var token in ids)
        {
            if (token.Type != JTokenType.Integer || !long.TryParse(token.ToString(), out var id) || id <= 0)
                throw new ArgumentException("elementIds must contain positive 64-bit integers without string or floating-point coercion.");
            if (seen.Add(id)) unique.Add(id);
        }
        return new ElementRelationshipQuery
        {
            ElementIds = unique.ToArray(), IncludeHostChain = Flag(input, "includeHostChain", true),
            IncludeSubComponents = Flag(input, "includeSubComponents", true), IncludeInserts = Flag(input, "includeInserts", true),
            IncludeDependents = Flag(input, "includeDependents", false), IncludeAdaptivePoints = Flag(input, "includeAdaptivePoints", true),
            MaxDepth = Number(input, "maxDepth", 8, 16), MaxRelated = Number(input, "maxRelated", 50, 100)
        };
    }

    private static bool Flag(JObject input, string key, bool fallback) => input[key] == null || input[key]!.Type == JTokenType.Null
        ? fallback : input[key]!.Type == JTokenType.Boolean ? input[key]!.Value<bool>() : throw new ArgumentException($"{key} must be boolean.");
    private static int Number(JObject input, string key, int fallback, int max)
    {
        var value = input[key];
        if (value == null || value.Type == JTokenType.Null) return fallback;
        if (value.Type != JTokenType.Integer || !int.TryParse(value.ToString(), out var n) || n < 1 || n > max)
            throw new ArgumentException($"{key} must be an integer in 1..{max}.");
        return n;
    }
}

internal sealed record ElementRelationshipEdge(long ElementId, string Kind, bool IsLinkBoundary);
internal sealed record ElementRelationshipChain(IReadOnlyList<ElementRelationshipEdge> Edges, string Termination)
{
    internal bool Truncated => Termination is "depth_limit" or "link_boundary";

    internal static ElementRelationshipChain Walk(long startId, int maxDepth, Func<long, ElementRelationshipEdge?> next)
    {
        var edges = new List<ElementRelationshipEdge>();
        var visited = new HashSet<long> { startId };
        var current = startId;
        for (var depth = 0; depth < maxDepth; depth++)
        {
            var edge = next(current);
            if (edge == null) return new ElementRelationshipChain(edges, "root");
            if (!visited.Add(edge.ElementId)) return new ElementRelationshipChain(edges, "cycle");
            edges.Add(edge);
            if (edge.IsLinkBoundary) return new ElementRelationshipChain(edges, "link_boundary");
            current = edge.ElementId;
        }
        return new ElementRelationshipChain(edges, "depth_limit");
    }
}

internal sealed class ElementRelationshipResults
{
    // Reserve another 100 kB for the envelope and at most 100 small not_read receipts.
    internal const int DetailBudgetBytes = 900_000;
    internal JArray Records { get; } = new();
    internal bool Truncated { get; private set; }
    private int _bytes;

    internal void Add(long id, JObject? record)
    {
        var bytes = record == null ? 0 : Encoding.UTF8.GetByteCount(record.ToString(Formatting.None));
        if (Truncated || record == null || _bytes + bytes > DetailBudgetBytes)
        {
            Truncated = true;
            Records.Add(new JObject { ["elementId"] = id, ["status"] = "not_read", ["reason"] = "ResponseBudgetExceeded" });
            return;
        }
        _bytes += bytes;
        Records.Add(record);
    }
}
