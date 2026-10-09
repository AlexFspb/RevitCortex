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
            Name = $"RevitCortex-{port}",
            Version = "2.0.0"
        };
        options.ServerInstructions =
            $"This MCP connection is fixed to localhost:{port}. Honor the user's requested port; never switch ports on failure. " +
            "Use get_connection_status even with no document; it returns a cached UI snapshot. list_revit_instances discovers endpoints without rebinding. " +
            "Before model work use get_project_info (or say_hello if no document) and verify port, PID and document. A target mismatch blocks commands until explicit inspection. " +
            "If only a port was specified and Revit is empty, the first C# request prepares an empty service project automatically. " +
            "If the user named an existing project, locate and verify that project; never use a service project as its substitute. " +
            "This RevitCortex fork targets Autodesk Revit 2026. Prefer the dedicated Revit tool that matches the task " +
            "(parameters, filtering/queries, model statistics, views, schedules, tags, dimensions, rebar, steel, IFC, Power BI). " +
            "For destructive tools, use dryRun/preview first when the tool supports it. " +
            "send_code_to_revit is a LAST RESORT: do not select it autonomously when a dedicated tool covers the operation. " +
            "Use custom C# only for an operation that is genuinely uncovered, within the user-authorized task; " +
            "no separate chat approval is required. " +
            "Document.EditFamily is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. For custom family-edit scripts use transactionMode=none, not auto/group. Cortex rejects script entry while the active document has an open transaction. Direct Document.EditFamily calls are semantically checked during a single compilation before confirmation and persistence and routed through runtime document/family checks; auto/group, method-group capture and conditional EditFamily access are rejected. In an empty Revit, the MCP server first prepares a private empty metric service project. Preparation may create an RVT even if later compilation fails; the script itself has not run. Never use a service project instead of a user-named working model. Critical auto-run defaults to ON with a visible 3-second countdown; unchecking opts out for this process.  Scripts are persisted only after successful compilation and confirmation. Rejected scripts are not saved, even with reusable=true, and have no scriptPath. ScriptPreconditionFailed distinguishes scriptExecuted=false from earlier effects that may remain when a runtime call is blocked. This does not inspect calls hidden in precompiled libraries or replace native Revit validation. Before EditFamily check source IsValidObject, IsModifiable=false, IsReadOnly=false, family ownership, IsEditable=true and IsInPlace=false; never call during dynamic update. Use separate family-document transactions with failure handling configured before changes, check transaction status, and close the family document in finally after resolving its transactions. For the family workflow finish all transactions and groups (a Cortex workflow rule; LoadFamily itself requires the target not to be modifiable). Use familyDocument.LoadFamily(target, new RevitCortex.Tools.CodeExecution.FamilyLoadPolicy(overwriteParameterValues: chosenValue, useProjectSharedFamilies: chosenValue)); choose both booleans from the task. This IFamilyLoadOptions implementation continues reload conflicts with those choices and never opens UI. For auto mode use strictWarnings=true when the task requires rollback on warnings; its default false preserves warning-compatible behavior. strictWarnings=true is rejected in none/group: instead use ScriptFailureHandling.Configure(tx, rollbackOnWarnings: true) for each owned transaction; Configure(tx) retains warning-compatible behavior. In none/group include capture.Failures, capture.OmittedFailures and capture.DiagnosticReportPath in the returned plain data yourself; Cortex does not append them. SaveAs needs a literal path in an existing folder and OverwriteExistingFile=false unless overwrite was authorized; file/backup checks happen outside the sandbox. Check the bool returned by Close(false); false means cleanup failed. Never open UI dialogs or force-accept unresolved failures. Return only materialized plain data. none cannot roll back earlier commits, saved files or reloads; a timeout does not abort running Revit code. Verify state before any retry. " +
            "Custom C# remains gated by settings, sandbox validation, audit logging and a " +
            "critical Revit confirmation. The critical dialog defaults to a visible 3-second auto-run countdown on each Revit launch. Unchecking disables auto-run for this process; No/X cancels. No approval flags are persisted.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
