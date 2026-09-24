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
            "no separate chat approval is required. Never use Document.EditFamily from the external-event execution context because modal " +
            "family editing can deadlock the request. Custom C# remains gated by settings, sandbox validation, audit logging and a " +
            "critical Revit confirmation. The user may explicitly enable the fork's session-only 3-second auto-run countdown in that dialog.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
