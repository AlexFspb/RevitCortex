using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Addins;

/// <summary>Shared with a trusted installed add-in, not with arbitrary scripts. API version 1.</summary>
public interface IAddinCommandProvider
{
    AddinProviderInfo Describe();
    // Called in Revit API context. The UI and programmatic path must share this gate.
    bool TryEnter(out IDisposable? lease);
    AddinCommandResult Execute(AddinExecutionContext context);
}

/// <summary>Implemented by a stable loader. Must throw on failure, never merely log it.</summary>
public interface IReloadableAddinProvider : IAddinCommandProvider
{
    IAddinCommandProvider Reload();
}

public sealed class AddinProviderInfo
{
    public string PluginId { get; set; } = "";
    public string Name { get; set; } = "";
    public int ApiVersion { get; set; } = 1;
    public string LoadedVersion { get; set; } = "";
    public string ModuleId { get; set; } = ""; // MVID of the actually loaded calculation module
    public string? LoadedModuleSha256 { get; set; } // Hash the exact image at load time, never a replacement file on disk.
    public AddinCommandInfo[] Operations { get; set; } = Array.Empty<AddinCommandInfo>();
}

public sealed class AddinCommandInfo
{
    public string Name { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string ArgumentsSchema { get; set; } = "";
    public string ResultSchema { get; set; } = "";
    public bool RequiresDocument { get; set; } = true;
    public bool MutatesDocument { get; set; }
    public bool ExternalEffects { get; set; }
    public bool SupportsCancellation { get; set; }
    public string TransactionOwner { get; set; } = "provider";
    public string[] Modes { get; set; } = new[] { "preview" };
}

public sealed class AddinExecutionContext
{
    public string OperationName { get; }
    public string Mode { get; }
    public string ArgumentsJson { get; }
    // UIApplication, only for synchronous use within Execute. Never pass to worker threads.
    public object? RevitApplication { get; }
    public CancellationToken Cancellation { get; }
    public Action<string> ReportStage { get; }
    public AddinExecutionContext(string operationName, string mode, string argumentsJson,
        object? revitApplication, CancellationToken cancellation, Action<string> reportStage)
    {
        OperationName = operationName; Mode = mode; ArgumentsJson = argumentsJson;
        RevitApplication = revitApplication; Cancellation = cancellation; ReportStage = reportStage;
    }
}

public sealed class AddinCommandResult
{
    public string Status { get; set; } = "failed";
    public string ResultJson { get; set; } = "{}";
    public string DiagnosticsJson { get; set; } = "[]";
    public string TransactionOutcome { get; set; } = "unknown";
    public bool? ModelChanged { get; set; }
    public bool? ExternalEffects { get; set; }
}

/// <summary>A synchronous rejection is distinct from an accepted operation's final outcome.</summary>
public sealed class AddinProtocolException : Exception
{
    public string Code { get; }
    public AddinProtocolException(string code, string message) : base(message) { Code = code; }
}

public interface IAddinScheduler
{
    bool TrySchedule(Func<object?, RevitCortex.Core.Results.CortexResult<object>> run,
        Action<RevitCortex.Core.Results.CortexResult<object>> completed);
}
