using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Caching;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Telemetry;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin.Threading;

namespace RevitCortex.Plugin;

public class CortexRouter
{
    private readonly Dictionary<string, ICortexTool> _tools = new();
    private readonly Dictionary<string, ToolSafetyRegistration> _toolSafety =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly CortexSession _session;
    private readonly IDocumentAnalyzer _analyzer;
    private readonly AuditLogger _auditLogger;
    private readonly ErrorReporter? _errorReporter;

    // Set once from OnStartup (UI thread) but read from socket worker threads.
    private volatile RevitThreadDispatcher? _dispatcher;

    // Captured when the dispatcher is wired in OnStartup. Used to detect callers
    // already on Revit's UI thread so we do not deadlock by waiting on ExternalEvent.
    private int _uiThreadId;

    // Copy-on-write: writers replace the complete set atomically, readers never see
    // a partially-mutated collection.
    private volatile HashSet<string> _disabledTools = new();
    private bool _readOnlyMode;

    /// <summary>
    /// Prefixes that identify read-only (query-only) tools.
    /// Tools matching these prefixes are allowed in read-only mode.
    /// </summary>
    private static readonly string[] ReadOnlyPrefixes = new[]
    {
        "get_", "list_", "find_", "analyze_", "check_",
        "measure_", "audit_", "export_", "say_hello",
        "clash_detection", "lines_per_view_count",
        "ifc_get_", "ifc_list_", "ifc_export_", "ifc_validate_",
        "ifc_analyze_", "ifc_compare_"
    };

    /// <summary>
    /// Write-named tools vetted for inline UI-thread execution: they open no
    /// Transaction and show no Revit UI. Keep this list minimal and audited.
    /// </summary>
    private static readonly HashSet<string> InlineUiThreadAllowedTools =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "push_to_powerbi",
    };

    private sealed class ToolSafetyRegistration
    {
        public ToolSafetyRegistration(bool readOnly, bool destructive, bool declared)
        {
            ReadOnly = readOnly;
            Destructive = destructive;
            Declared = declared;
        }

        public bool ReadOnly { get; }
        public bool Destructive { get; }
        public bool Declared { get; }
    }

    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer,
        AuditLogger? auditLogger = null, ErrorReporter? errorReporter = null)
    {
        _session = session;
        _analyzer = analyzer;
        _auditLogger = auditLogger ?? new AuditLogger();
        _errorReporter = errorReporter;
    }

    /// <summary>
    /// Scan an assembly for all ICortexTool implementations and register them.
    /// </summary>
    public void RegisterToolsFromAssembly(Assembly assembly)
    {
        var toolTypes = assembly.GetTypes()
            .Where(t => typeof(ICortexTool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in toolTypes)
        {
            try
            {
                var tool = (ICortexTool)Activator.CreateInstance(type)!;
                RegisterTool(tool, type);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Failed to register tool {type.Name}: {ex.Message}");
            }
        }
    }

    public void RegisterTool(ICortexTool tool)
    {
        RegisterTool(tool, tool.GetType());
    }

    private void RegisterTool(ICortexTool tool, Type toolType)
    {
        _tools[tool.Name] = tool;

        var safety = ResolveToolSafety(tool, toolType);
        _toolSafety[tool.Name] = safety;

        var prefixReadOnly = IsReadOnlyTool(tool.Name);
        if (safety.Declared && safety.ReadOnly != prefixReadOnly)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Tool safety mismatch for {tool.Name}: " +
                $"declared ReadOnly={safety.ReadOnly}, prefix ReadOnly={prefixReadOnly}.");
        }
        else if (!safety.Declared)
        {
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Tool {tool.Name} has no [ToolSafety]; using prefix fallback.");
        }
    }

    private static ToolSafetyRegistration ResolveToolSafety(ICortexTool tool, Type toolType)
    {
        var aware = tool as IToolSafetyAware;
        if (aware != null)
        {
            var info = aware.GetToolSafety();
            return new ToolSafetyRegistration(info.ReadOnly, info.Destructive, declared: true);
        }

        var attribute = (ToolSafetyAttribute?)Attribute.GetCustomAttribute(
            toolType, typeof(ToolSafetyAttribute), inherit: true);
        if (attribute != null)
        {
            return new ToolSafetyRegistration(
                attribute.ReadOnly, attribute.Destructive, declared: true);
        }

        return new ToolSafetyRegistration(
            IsReadOnlyTool(tool.Name), destructive: false, declared: false);
    }

    public CortexResult<object> Route(string toolName, JObject input)
    {
        var documentContext = _session.CaptureDocumentContext();
        var documentVersion = _session.DocumentVersion;
        if (!_tools.TryGetValue(toolName, out var tool))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' not found",
                suggestion: $"Available tools: {string.Join(", ", GetAvailableToolNames())}");

        if (_disabledTools.Contains(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is disabled",
                suggestion: "Enable it in RevitCortex Settings > Tools");

        if (tool.RequiresDocument && documentContext.Document == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No document open in Revit",
                suggestion: "Open a Revit document before using this tool");

        if (tool.IsDynamic && !_session.Capabilities.IsToolEnabled(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is not available for this document",
                suggestion: "This tool requires specific document features (e.g., worksets, phases)");

        // User-controlled read-only mode is the only global write gate in this fork.
        if (_readOnlyMode && !IsToolReadOnly(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"Tool '{toolName}' is blocked in read-only mode",
                suggestion: "Disable read-only mode in Settings to allow write operations");

        var stopwatch = Stopwatch.StartNew();
        CortexResult<object> result;

        // Cache lookup for read-only tools that opted into ICacheableTool.
        // On hit, skip the dispatcher entirely — no UI-thread marshal is needed
        // to return a previously-computed value.
        var cacheable = tool as ICacheableTool;
        string? paramHash = null;
        if (cacheable != null)
        {
            // A delayed response from a previous document must never populate
            // the new document's cache, including CacheScope.Session entries.
            paramHash = documentContext.Generation + ":" + HashParams(input);
            if (_session.Cache.TryGet(toolName, paramHash, cacheable.CacheScope,
                    documentVersion, out var cached, out var cachedBytes)
                && _session.IsCurrentDocumentContext(documentContext))
            {
                stopwatch.Stop();
                _auditLogger.LogWithPerf(toolName, BuildInputSummary(toolName, input),
                    cached.Success, cached.Error?.Code, elementsAffected: 0,
                    durationMs: stopwatch.ElapsedMilliseconds,
                    responseBytes: cachedBytes,
                    errorMessage: cached.Error?.Message);
                return cached;
            }
        }

        try
        {
            // Background callers go through ExternalEvent so the tool runs in a
            // valid Revit API context. UI-thread callers run inline only when safe.
            bool onUiThread = _dispatcher != null
                && System.Threading.Thread.CurrentThread.ManagedThreadId == _uiThreadId;

            if (_dispatcher != null && !onUiThread)
            {
                var timeoutSeconds = (tool as ICommandTimeoutTool)?.CommandTimeoutSeconds ?? 120;
                result = _dispatcher.Execute(tool, input, _session, timeoutSeconds * 1000, documentContext);
            }
            else if (onUiThread && !IsToolReadOnly(toolName)
                     && !InlineUiThreadAllowedTools.Contains(toolName))
            {
                result = CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                    $"Tool '{toolName}' cannot run inline on the UI thread outside a Revit API context",
                    suggestion: "Call the tool through the MCP/TCP bridge so it is dispatched via ExternalEvent.");
            }
            else
            {
                result = _session.IsCurrentDocumentContext(documentContext)
                    ? tool.Execute(input, _session)
                    : CortexResult<object>.Fail(CortexErrorCode.Cancelled,
                        "The active document changed before the command could start.");
            }
        }
        catch (Exception ex)
        {
            // Nothing may escape Route as a raw exception.
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Route('{toolName}') unhandled: {ex}");
            result = CortexResult<object>.Fail(CortexErrorCode.Unknown,
                $"Unhandled exception: {ex.Message}",
                suggestion: "Retry; if it persists, send a support report from the RevitCortex ribbon.");
        }
        finally
        {
            // Reset only the per-batch Yes-to-All flag. Auto mode persists until
            // explicitly stopped or the document is reinitialized.
            _session.ApproveAll = false;
        }

        var responseBytes = EstimateResponseBytes(result);

        if (cacheable != null && paramHash != null && result.Success)
        {
            _session.Cache.Set(toolName, paramHash, cacheable.CacheScope,
                documentVersion, result, knownBytes: responseBytes);
        }

        stopwatch.Stop();

        // Audit every invocation. send_code_to_revit also gets a truncated code
        // snapshot plus SHA-256 hash.
        var inputSummary = BuildInputSummary(toolName, input);
        string? codeSnippet = null;
        string? codeHash = null;
        if (toolName == "send_code_to_revit")
        {
            var code = input["code"]?.Value<string>();
            if (!string.IsNullOrEmpty(code))
            {
                codeSnippet = code!.Length <= 500 ? code : code.Substring(0, 500);
                codeHash = ComputeSha256(code!);
            }
        }

        _auditLogger.LogWithPerf(toolName, inputSummary, result.Success,
            result.Error?.Code, elementsAffected: 0,
            durationMs: stopwatch.ElapsedMilliseconds,
            responseBytes: responseBytes,
            codeSnippet: codeSnippet,
            codeHash: codeHash,
            errorMessage: result.Error?.Message);

        try
        {
            _errorReporter?.Record(toolName, result.Success,
                result.Error?.Code.ToString(), result.Error?.Message,
                failureStage: "tool",
                durationMs: stopwatch.ElapsedMilliseconds,
                responseBytes: responseBytes);
        }
        catch { /* telemetry must never change the returned result */ }

        return result;
    }

    private static long EstimateResponseBytes(CortexResult<object> result)
    {
        try
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            return Encoding.UTF8.GetByteCount(json);
        }
        catch
        {
            return 0;
        }
    }

    private static string ComputeSha256(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Canonical SHA-256 of a tool's input. Keys are sorted recursively and
    /// the JSON is emitted without whitespace, so calls that differ only in
    /// key order or formatting hit the same cache entry.
    /// </summary>
    internal static string HashParams(JObject input)
    {
        var sw = new System.IO.StringWriter(new StringBuilder(256),
            System.Globalization.CultureInfo.InvariantCulture);
        using (var writer = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            WriteCanonical(writer, input);
        }
        return ComputeSha256(sw.ToString());
    }

    private static void WriteCanonical(JsonTextWriter writer, JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                writer.WriteStartObject();
                foreach (var prop in ((JObject)token).Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(prop.Name);
                    WriteCanonical(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JTokenType.Array:
                writer.WriteStartArray();
                foreach (var item in (JArray)token)
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                token.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// Determines if a tool is read-only (query-only) based on naming convention.
    /// </summary>
    public static bool IsReadOnlyTool(string toolName)
    {
        foreach (var prefix in ReadOnlyPrefixes)
        {
            if (toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool IsToolReadOnly(string toolName)
    {
        return _toolSafety.TryGetValue(toolName, out var safety)
            ? safety.ReadOnly
            : IsReadOnlyTool(toolName);
    }

    public bool IsToolDestructive(string toolName)
    {
        return _toolSafety.TryGetValue(toolName, out var safety) && safety.Destructive;
    }

    public bool ReadOnlyMode
    {
        get => _readOnlyMode;
        set => _readOnlyMode = value;
    }

    private static string BuildInputSummary(string toolName, JObject input)
    {
        if (input == null || !input.HasValues) return "(no params)";

        var parts = new List<string>(input.Count);
        foreach (var prop in input.Properties())
        {
            parts.Add($"{prop.Name}={FormatValue(prop.Name, prop.Value)}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "(no params)";
    }

    private static string FormatValue(string name, JToken token)
    {
        if (token.Type == JTokenType.String &&
            (name == "code" || name == "snippet"))
        {
            return $"({token.ToString().Length} chars)";
        }

        switch (token.Type)
        {
            case JTokenType.Null:
            case JTokenType.Undefined:
                return "null";
            case JTokenType.Array:
                var arr = (JArray)token;
                return $"[{arr.Count} items]";
            case JTokenType.Object:
                return token.ToString(Newtonsoft.Json.Formatting.None);
            case JTokenType.String:
                var s = token.ToString();
                return s.Length > 80 ? s.Substring(0, 80) + "..." : s;
            default:
                return token.ToString();
        }
    }

    public void SetDispatcher(RevitThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _uiThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
    }

    public void OnDocumentChanged(object document, string? locale = null)
    {
        var caps = new DocumentCapabilities();
        _analyzer.Analyze(document, caps);

        _session.Reinitialize(caps, locale ?? "en", document);
    }

    /// <summary>UI-thread only. Closing a background family must not reset the project.</summary>
    public void OnDocumentClosing(object document)
    {
        if (ReferenceEquals(_session.CaptureDocumentContext().Document, document))
            _session.Reinitialize(new DocumentCapabilities(), "en");
    }

    /// <summary>Synchronize from ActiveUIDocument, including after a cancelled close.</summary>
    public void SynchronizeActiveDocument(object? document, string? locale = null)
    {
        if (ReferenceEquals(_session.CaptureDocumentContext().Document, document)) return;
        if (document == null) _session.Reinitialize(new DocumentCapabilities(), "en");
        else OnDocumentChanged(document, locale);
    }

    public IReadOnlyList<string> GetAvailableToolNames()
    {
        return _tools.Values
            .Where(t => !_disabledTools.Contains(t.Name))
            .Where(t => !t.IsDynamic || _session.Capabilities.IsToolEnabled(t.Name))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();
    }

    public int TotalToolCount => _tools.Count;

    /// <summary>
    /// Returns all registered tools with their name, category, description, and enabled state.
    /// </summary>
    public IReadOnlyList<(string Name, string Category, string Description, bool IsEnabled)> GetAllToolInfo()
    {
        return _tools.Values
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .Select(t => (t.Name, t.Category, t.Description, !_disabledTools.Contains(t.Name)))
            .ToList();
    }

    public void SetDisabledTools(IEnumerable<string> toolNames)
    {
        _disabledTools = new HashSet<string>(toolNames);
    }

    public IReadOnlyCollection<string> DisabledTools => _disabledTools;
}
