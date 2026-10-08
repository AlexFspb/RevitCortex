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
            "Document.EditFamily is supported in a valid ExternalEvent API context; it is not inherently a modal UI command. For custom family-edit scripts use transactionMode=none, not auto/group. Before EditFamily check source IsValidObject, IsModifiable=false, IsReadOnly=false, family ownership, IsEditable=true and IsInPlace=false; never call during dynamic update. Use separate family-document transactions with failure handling configured before changes, check transaction status, and close the family document in finally after resolving its transactions. To reload, finish all transactions and transaction groups and use LoadFamily with explicit IFamilyLoadOptions; never open UI dialogs or force-accept unresolved failures. Return only materialized plain data. none cannot roll back earlier commits, saved files or reloads; a timeout does not abort running Revit code. Verify state before any retry. " +
            "Custom C# remains gated by settings, sandbox validation, audit logging and a " +
            "critical Revit confirmation. The user may explicitly enable the fork's session-only 3-second auto-run countdown in that dialog.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
