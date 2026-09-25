using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Results;

/// <summary>Bounded primitive diagnostics; no dependency on the Revit API.</summary>
public sealed class ScriptFailureReport
{
    public const int ReserveBytes = 64 * 1024;
    private int _serializedBytes = 2;
    private readonly List<Dictionary<string, object>> _failures = new();
    public IReadOnlyList<Dictionary<string, object>> Failures => _failures;
    public int OmittedFailures { get; private set; }
    public int WarningCount { get; private set; }
    public bool HasErrors { get; private set; }

    // Return true only for warnings that may be removed from Revit's failure dialog.
    public bool Record(string severity, string description, IEnumerable<long> elementIds)
    {
        if (severity == "None") return false;
        bool warning = severity == "Warning";
        if (warning) WarningCount++; else HasErrors = true;
        if (_failures.Count >= 100) { OmittedFailures++; return warning; }
        var ids = elementIds.Take(201).ToArray();
        var entry = new Dictionary<string, object>
        {
            ["description"] = description.Length > 2048 ? description.Substring(0, 2048) : description,
            ["descriptionTruncated"] = description.Length > 2048,
            ["severity"] = severity,
            ["elementIds"] = ids.Take(200).ToArray(),
            ["elementIdsTruncated"] = ids.Length > 200
        };
        // EscapeNonAscii is an upper bound for the final UTF-8 representation.
        int size = JsonConvert.SerializeObject(entry, new JsonSerializerSettings
            { StringEscapeHandling = StringEscapeHandling.EscapeNonAscii }).Length + 1;
        if (_serializedBytes + size > ReserveBytes - 8192) { OmittedFailures++; return warning; }
        _serializedBytes += size;
        _failures.Add(entry);
        return warning;
    }

    public void AppendWarnings(JObject response, string? reportPath)
    {
        if (WarningCount == 0) return;
        response["warnings"] = JArray.FromObject(_failures.Where(f => (string)f["severity"] == "Warning"));
        response["warningCount"] = WarningCount;
        response["omittedFailures"] = OmittedFailures;
        response["diagnosticReportPath"] = reportPath == null ? "(report could not be saved)"
            : reportPath.Length <= 1024 ? reportPath : reportPath.Substring(0, 1024);
    }
}
