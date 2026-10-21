using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using RevitCortex.Server.Connection;

namespace RevitCortex.Server.Tools;

[McpServerToolType]
public static class MetaTools
{
    [McpServerTool(Name = "get_connection_status"), Description("Return this connection's port and the plugin's cached PID, instanceId, document title/path/generation, service-project flag and build. Works without a document and does not wait for the Revit UI. snapshotOnly=true means this is not proof the UI is responsive. Does not rebind the selected target.")]
    public static async Task<string> GetConnectionStatus(RevitConnectionManager revit, CancellationToken ct = default)
    {
        try { return (await revit.ExecuteAsync("get_connection_status", new JObject(), ct)).ToString(); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        { return new JObject { ["configuredPort"] = revit.Port, ["status"] = "unavailable", ["message"] = ex.Message }.ToString(); }
    }

    [McpServerTool(Name = "list_revit_instances"), Description("Discover Cortex on four Release ports (8080,8888,8880,8088), four Dev ports (8081,8889,8083,8891) and this connection's port. Returns cached port/PID/document/service-project metadata, or unavailable. Read-only; never changes the current connection. Use the separately configured MCP connection for the user's requested port.")]
    public static async Task<string> ListRevitInstances(RevitConnectionManager revit,
        [Description("Optional explicit ports to inspect, at most 16. Default: known Cortex ports plus this connection.")] int[]? ports = null, CancellationToken ct = default) =>
        (await revit.DiscoverAsync(ports, ct)).ToString();

    [McpServerTool(Name = "ensure_service_document"), Description("Prepare an empty metric service project in this Revit only if no document is active. Never switch away from an existing document. Returns port/PID/document identity and isServiceDocument. Service files are retained, never deleted automatically. Do not use as a substitute for a user-named working project. The first C# request in empty Revit already performs this preparation automatically.")]
    public static async Task<string> EnsureServiceDocument(RevitConnectionManager revit, CancellationToken ct = default) =>
        (await revit.ExecuteAsync("ensure_service_document", new JObject(), ct)).ToString();

    [McpServerTool(Name = "say_hello"), Description("Identify and explicitly rebind this MCP connection to the current Revit instance/document. Returns PID, port, instanceId, title/path, generation and service-project flag. Works without a document. Verify the result matches the user request. No dialog is shown.")]
    public static async Task<string> SayHello(RevitConnectionManager revit, CancellationToken ct)
    {
        var result = await revit.ExecuteAsync("say_hello", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_project_info"), Description("Get port, Revit PID, instanceId, active document title/path, generation, isServiceDocument, and project metadata. Explicitly verifies/rebinds this connection to the current active document; check it matches the user request before mutations.")]
    public static async Task<string> GetProjectInfo(
        RevitConnectionManager revit,
        [Description("Include levels in the response")] bool includeLevels = true,
        [Description("Include phases in the response")] bool includePhases = true,
        [Description("Include worksets in the response")] bool includeWorksets = false,
        [Description("Include linked models in the response")] bool includeLinks = false,
        CancellationToken ct = default)
    {
        var p = new JObject
        {
            ["includeLevels"] = includeLevels,
            ["includePhases"] = includePhases,
            ["includeWorksets"] = includeWorksets,
            ["includeLinks"] = includeLinks,
        };
        var result = await revit.ExecuteAsync("get_project_info", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "set_project_info"), Description("Set editable Project Information fields. Only the fields you pass are changed; others are left untouched.")]
    public static async Task<string> SetProjectInfo(
        RevitConnectionManager revit,
        [Description("Project name")] string? projectName = null,
        [Description("Project number")] string? projectNumber = null,
        [Description("Project address")] string? projectAddress = null,
        [Description("Building name")] string? buildingName = null,
        [Description("Author")] string? author = null,
        [Description("Organization name")] string? organizationName = null,
        [Description("Organization description")] string? organizationDescription = null,
        [Description("Issue date")] string? issueDate = null,
        [Description("Project status")] string? status = null,
        [Description("Client name (Owner)")] string? clientName = null,
        CancellationToken ct = default)
    {
        var p = new JObject();
        if (projectName != null) p["projectName"] = projectName;
        if (projectNumber != null) p["projectNumber"] = projectNumber;
        if (projectAddress != null) p["projectAddress"] = projectAddress;
        if (buildingName != null) p["buildingName"] = buildingName;
        if (author != null) p["author"] = author;
        if (organizationName != null) p["organizationName"] = organizationName;
        if (organizationDescription != null) p["organizationDescription"] = organizationDescription;
        if (issueDate != null) p["issueDate"] = issueDate;
        if (status != null) p["status"] = status;
        if (clientName != null) p["clientName"] = clientName;
        var result = await revit.ExecuteAsync("set_project_info", p, ct);
        return result.ToString();
    }

    [McpServerTool(Name = "get_cache_stats"), Description("Return diagnostic hit/miss telemetry from the plugin-side tool-result cache.")]
    public static async Task<string> GetCacheStats(RevitConnectionManager revit, CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("get_cache_stats", new JObject(), ct);
        return result.ToString();
    }

    [McpServerTool(Name = "clear_cache"), Description("Clear every entry from the plugin-side tool-result cache.")]
    public static async Task<string> ClearCache(RevitConnectionManager revit, CancellationToken ct = default)
    {
        var result = await revit.ExecuteAsync("clear_cache", new JObject(), ct);
        return result.ToString();
    }
}
