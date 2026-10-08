using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RevitCortex.Server.Connection;

var port = RevitConnectionManager.ResolvePort();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new RevitConnectionManager(port));
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "RevitCortex",
            Version = "2.0.0"
        };
        options.ServerInstructions =
            "This RevitCortex fork targets Autodesk Revit 2026. Prefer the dedicated Revit tool that matches the task " +
            "(parameters, filtering/queries, model statistics, views, schedules, tags, dimensions, rebar, steel, IFC, Power BI). " +
            "For destructive tools, use dryRun/preview first when the tool supports it. " +
            "send_code_to_revit is a LAST RESORT: do not select it autonomously when a dedicated tool covers the operation. " +
            "Use custom C# only for an operation that is genuinely uncovered, within the user-authorized task; " +
            "no separate chat approval is required. " +
            "Document.EditFamily is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. For custom family-edit scripts use transactionMode=none, not auto/group. Cortex rejects script entry while the active document has an open transaction. Direct Document.EditFamily calls are semantically checked before execution and routed through runtime document/family checks; auto/group, method-group capture and conditional EditFamily access are rejected. ScriptPreconditionFailed distinguishes scriptExecuted=false from earlier effects that may remain when a runtime call is blocked. This does not inspect calls hidden in precompiled libraries or replace native Revit validation. Before EditFamily check source IsValidObject, IsModifiable=false, IsReadOnly=false, family ownership, IsEditable=true and IsInPlace=false; never call during dynamic update. Use separate family-document transactions with failure handling configured before changes, check transaction status, and close the family document in finally after resolving its transactions. For the family workflow finish all transactions and groups (a Cortex workflow rule; LoadFamily itself requires the target not to be modifiable). Use familyDocument.LoadFamily(target, new RevitCortex.Tools.CodeExecution.FamilyLoadPolicy(overwriteParameterValues: chosenValue, useProjectSharedFamilies: chosenValue)); choose both booleans from the task. This IFamilyLoadOptions implementation continues reload conflicts with those choices and never opens UI. For strict warning rollback use ScriptFailureHandling.Configure(tx, rollbackOnWarnings: true); Configure(tx) retains warning-compatible behavior. In none/group include capture.Failures, capture.OmittedFailures and capture.DiagnosticReportPath in the returned plain data yourself; Cortex does not append them. SaveAs needs a literal path in an existing folder and OverwriteExistingFile=false unless overwrite was authorized; file/backup checks happen outside the sandbox. Check the bool returned by Close(false); false means cleanup failed. Never open UI dialogs or force-accept unresolved failures. Return only materialized plain data. none cannot roll back earlier commits, saved files or reloads; a timeout does not abort running Revit code. Verify state before any retry. " +
            "Custom C# remains gated by settings, sandbox validation, audit logging and a " +
            "critical Revit confirmation. The user may explicitly enable the fork's session-only 3-second auto-run countdown in that dialog.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
