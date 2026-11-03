using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class StructuralMemberTools
{
    [McpServerTool(Name = "get_location_curves"), Description("Read actual location geometry and identity of structural beams/columns on this fixed Revit port. Coordinates are Revit internal feet; type parameter values use their reported data types. Distinguishes exact axes from point-only/unsupported locations; bounding boxes are not axes. Categories: StructuralFraming, StructuralColumns. Stable element-ID ordering; paginate with nextOffset. Up to 500 rows per page and 5000 explicit elementIds. Geometry is not a structural design or load-bearing check.")]
    public static async Task<string> GetLocationCurves(
        RevitConnectionManager revit,
        [Description("Optional categories; default StructuralFraming and StructuralColumns")] string[]? categories = null,
        [Description("Optional explicit IDs, at most 5000; missing IDs are diagnosed")] long[]? elementIds = null,
        [Description("Include bounded numeric type parameters with data type metadata. Default false")] bool includeTypeParams = false,
        [Description("Page offset; use nextOffset from the preceding response")] int offset = 0,
        [Description("Page size, 1..500. Default 500")] int limit = 500,
        CancellationToken ct = default)
    {
        var input = new JObject { ["includeTypeParams"] = includeTypeParams, ["offset"] = offset, ["limit"] = limit };
        if (categories != null) input["categories"] = new JArray(categories);
        if (elementIds != null) input["elementIds"] = new JArray(elementIds);
        return (await revit.ExecuteAsync("get_location_curves", input, ct)).ToString();
    }

    [McpServerTool(Name = "author_structural_members"), Description("Create a bounded atomic batch of straight beams and vertical structural columns from already loaded families. membersJson is an array (max 100): [{sourceId,role:'beam'|'column',family,baseType,typeName,p0_ft:[x,y,z],p1_ft:[x,y,z],typeParams:{},instanceParams:{},levelName?}]. Coordinates and numeric parameters use Revit internal units, not mm. Preview is the default (dryRun=true); set false only after inspecting preview. Existing types are never modified; conflicting dimensions or unsupported geometry fail. Any member error or Revit warning rolls back the whole write batch, with primitive diagnostics and actual rollback state. pin defaults false. Not a structural engineering calculation. A timeout/unknown rollback requires inspecting model state; never blindly repeat a write.")]
    public static async Task<string> AuthorStructuralMembers(
        RevitConnectionManager revit,
        [Description("JSON array of at most 100 member specifications; unique sourceId per member")] string membersJson,
        [Description("Preview without model changes. Default true; false applies the whole batch atomically")] bool dryRun = true,
        [Description("Pin newly created instances. Default false")] bool pin = false,
        CancellationToken ct = default)
    {
        JArray members;
        try
        {
            if (string.IsNullOrWhiteSpace(membersJson) || Encoding.UTF8.GetByteCount(membersJson) > 1024 * 1024)
                throw new ArgumentException("membersJson must be a nonempty JSON array of at most 1 MiB.");
            using var text = new StringReader(membersJson);
            using var reader = new JsonTextReader(text) { MaxDepth = 16, DateParseHandling = DateParseHandling.None };
            members = JArray.Load(reader, new JsonLoadSettings
            {
                DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                LineInfoHandling = LineInfoHandling.Ignore,
                CommentHandling = CommentHandling.Ignore
            });
            if (reader.Read()) throw new ArgumentException("membersJson must contain exactly one JSON array.");
            if (members.Count < 1 || members.Count > 100)
                throw new ArgumentException("Supply 1..100 members.");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return JsonConvert.SerializeObject(CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                ex.Message, context: new Dictionary<string, object> { ["commandSent"] = false }));
        }
        var input = new JObject { ["members"] = members, ["dryRun"] = dryRun, ["pin"] = pin };
        return (await revit.ExecuteAsync("author_structural_members", input, ct)).ToString();
    }
}
