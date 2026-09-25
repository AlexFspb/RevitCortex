using System;
using System.IO;
using System.Diagnostics;
using RevitCortex.Core.Hosting;
using Newtonsoft.Json;
using RevitCortex.Core.Results;

namespace RevitCortex.Core.Security;

/// <summary>
/// Append-only audit logger for tool operations.
/// Writes structured JSON lines to ~/.revitcortex/audit.jsonl.
/// Designed for ISO 19650 accountability: who did what, when, on which elements.
/// </summary>
public class AuditLogger
{
    private readonly string _logPath;
    private readonly object _lock = new object();

    public AuditLogger(string? logPath = null)
    {
        _logPath = logPath ?? CortexEnvironment.Current.AuditLogPath;
    }

    /// <summary>
    /// Log a tool execution to the audit trail (legacy overload, schema v1).
    /// </summary>
    public void Log(string toolName, string inputSummary, bool success,
        CortexErrorCode? errorCode = null, int elementsAffected = 0)
    {
        WriteEntry(new AuditEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Tool = toolName,
            InputSummary = Truncate(inputSummary, 500),
            Result = success ? "ok" : "fail",
            ErrorCode = errorCode?.ToString(),
            ElementsAffected = elementsAffected
        }, toolName);
    }

    /// <summary>
    /// Log a tool execution with performance data and optional send_code_to_revit
    /// snippet/hash (schema v2). Used by CortexRouter so rclog can diagnose
    /// perf bottlenecks and token-heavy tools.
    /// errorMessage is the human-readable failure detail (truncated to 200 chars)
    /// and lets triage distinguish e.g. "Unhandled exception: NRE" from
    /// "No result from tool execution" when both surface as Unknown.
    /// </summary>
    public void LogWithPerf(string toolName, string inputSummary, bool success,
        CortexErrorCode? errorCode = null, int elementsAffected = 0,
        long? durationMs = null, long? responseBytes = null,
        string? codeSnippet = null, string? codeHash = null,
        string? errorMessage = null)
    {
        WriteEntry(new AuditEntryV2
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            SchemaVersion = 2,
            Tool = toolName,
            InputSummary = Truncate(inputSummary, 500),
            Result = success ? "ok" : "fail",
            ErrorCode = errorCode?.ToString(),
            ErrorMessage = success ? null : Truncate(errorMessage ?? "", 200),
            ElementsAffected = elementsAffected,
            DurationMs = durationMs,
            ResponseBytes = responseBytes,
            CodeSnippet = codeSnippet,
            CodeHash = codeHash
        }, toolName);
    }

    /// <summary>Per-process request journal; separate from legacy audit consumers.</summary>
    public string RequestLogPath => Path.Combine(Path.GetDirectoryName(_logPath) ?? ".",
        Path.GetFileNameWithoutExtension(_logPath) + ".requests-" + Process.GetCurrentProcess().Id + ".jsonl");

    public void LogRequest(string requestId, string phase, string toolName, int? port,
        string? documentTitle, long documentGeneration, string inputSummary,
        string? codeHash = null, CortexResult<object>? response = null, long? durationMs = null)
    {
        WriteEntry(new
        {
            ts = DateTime.UtcNow.ToString("o"), v = 3, requestId, phase,
            revitProcessId = Process.GetCurrentProcess().Id, bridgePort = port,
            buildId = CortexBuild.Id, coreModuleId = CortexBuild.CoreModuleId,
            activeDocumentTitle = documentTitle, documentGeneration,
            tool = toolName, input_summary = Truncate(inputSummary, 500), codeHash,
            result = response == null ? "started" : response.Success ? "ok" : "fail",
            error_code = response?.Error?.Code.ToString(),
            error_message = response?.Error == null ? null : Truncate(response.Error.Message, 4096), duration_ms = durationMs,
            // Timeout is a response, not proof that the Revit operation has stopped.
            executionMayStillBeRunning = response?.Error?.Code == CortexErrorCode.Timeout
        }, toolName, RequestLogPath);
    }

    private void WriteEntry(object entry, string toolName, string? destination = null)
    {
        try
        {
            var json = JsonConvert.SerializeObject(entry, Formatting.None);
            var path = destination ?? _logPath;
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(path);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.AppendAllText(path, json + Environment.NewLine);
            }
        }
        catch
        {
            // Audit logging must never crash the application.
            System.Diagnostics.Trace.WriteLine(
                $"[RevitCortex] Failed to write audit log entry for tool '{toolName}'");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
    }

    private class AuditEntry
    {
        [JsonProperty("ts")] public string Timestamp { get; set; } = "";
        [JsonProperty("tool")] public string Tool { get; set; } = "";
        [JsonProperty("input_summary")] public string InputSummary { get; set; } = "";
        [JsonProperty("result")] public string Result { get; set; } = "";
        [JsonProperty("error_code", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; set; }
        [JsonProperty("elements_affected")] public int ElementsAffected { get; set; }
    }

    private class AuditEntryV2
    {
        [JsonProperty("ts")] public string Timestamp { get; set; } = "";
        [JsonProperty("v")] public int SchemaVersion { get; set; }
        [JsonProperty("tool")] public string Tool { get; set; } = "";
        [JsonProperty("input_summary")] public string InputSummary { get; set; } = "";
        [JsonProperty("result")] public string Result { get; set; } = "";
        [JsonProperty("error_code", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorCode { get; set; }
        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string? ErrorMessage { get; set; }
        [JsonProperty("elements_affected")] public int ElementsAffected { get; set; }
        [JsonProperty("duration_ms", NullValueHandling = NullValueHandling.Ignore)]
        public long? DurationMs { get; set; }
        [JsonProperty("response_bytes", NullValueHandling = NullValueHandling.Ignore)]
        public long? ResponseBytes { get; set; }
        [JsonProperty("code_snippet", NullValueHandling = NullValueHandling.Ignore)]
        public string? CodeSnippet { get; set; }
        [JsonProperty("code_hash", NullValueHandling = NullValueHandling.Ignore)]
        public string? CodeHash { get; set; }
    }
}
