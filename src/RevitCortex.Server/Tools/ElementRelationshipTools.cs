using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class ElementRelationshipTools
{
    [McpServerTool(Name = "get_element_relationships"), Description("Read host, parent family, group/assembly, host chain, nested components, hosted inserts and optional dependents/adaptive placement points for up to 100 explicit active-document element IDs. Bounded depth and related lists; returns field-level diagnostics and truncation rather than treating failed reads as empty. Points use mm relative to Revit internal origin. Does not traverse linked documents or treat IDs from a link as host-document IDs. Revit GetDependentElements is not a complete inventory of every semantic relation. Read-only; does not select elements, activate windows or edit families.")]
    public static async Task<string> GetElementRelationships(
        RevitConnectionManager revit,
        [Description("Up to 100 positive IDs from the active document")] long[] elementIds,
        [Description("Include a bounded chain of hosts/parent components. Default true")] bool includeHostChain = true,
        [Description("Host chain depth, 1..16. Default 8")] int maxDepth = 8,
        [Description("Include nested shared components. Default true")] bool includeSubComponents = true,
        [Description("Include inserts of supported host objects. Default true")] bool includeInserts = true,
        [Description("Include Revit GetDependentElements results. Default false")] bool includeDependents = false,
        [Description("Include adaptive component placement points. Default true")] bool includeAdaptivePoints = true,
        [Description("Per-related-list limit, 1..100. Default 50")] int maxRelated = 50,
        CancellationToken ct = default)
    {
        var input = new JObject
        {
            ["elementIds"] = new JArray(elementIds), ["includeHostChain"] = includeHostChain,
            ["maxDepth"] = maxDepth, ["includeSubComponents"] = includeSubComponents,
            ["includeInserts"] = includeInserts, ["includeDependents"] = includeDependents,
            ["includeAdaptivePoints"] = includeAdaptivePoints, ["maxRelated"] = maxRelated
        };
        return (await revit.ExecuteAsync("get_element_relationships", input, ct)).ToString();
    }
}
