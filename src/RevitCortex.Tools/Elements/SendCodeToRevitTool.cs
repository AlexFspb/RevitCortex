using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.CodeExecution;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace RevitCortex.Tools.Elements;

/// <summary>
/// Executes custom C# code snippets in the Revit 2026 context.
/// This fork targets Revit 2026 / .NET 8 and uses the Roslyn executor.
/// HARD-GATED by CortexSettings.EnableCodeExecution — default false.
/// CortexRouter records every invocation in audit.jsonl with code snippet + SHA-256.
/// Scripts are persisted to ~/.revitcortex/scripts/ and cleaned up at Revit shutdown
/// unless marked as reusable.
/// </summary>
[ToolSafety(false, true)]
public class SendCodeToRevitTool : ICortexTool
{
    public static string ScriptsFolder => CortexEnvironment.Current.ScriptsFolder;

    public string Name => "send_code_to_revit";
    public string Category => "Code";
    public bool RequiresDocument => true;
    public bool IsDynamic => false;
    public string Description => "LAST RESORT ONLY — execute custom C# code in the Revit context. Prefer dedicated tools always; use ONLY when no dedicated tool covers the operation within the user-authorized task. No separate chat approval is required; the configured Revit confirmation still applies. For every script-owned mutation transaction in group/none, call RevitCortex.Tools.CodeExecution.ScriptFailureHandling.Configure(tx) after Start and before changes. Auto mode configures it automatically. Errors roll back; warnings are removed from the Revit failure dialog and returned with descriptions and element IDs; inspect descriptions, severity and element IDs, verify commit status and retain the diagnostic report. Use a bounded dry-run before bulk replacement. Never force-accept errors or delete affected elements as recovery. Return plain data (scalars, anonymous objects, string-keyed dictionaries, arrays or LINQ), ElementId is converted to its 64-bit Value; other raw Revit objects are rejected. ResultSerializationFailed reports the result path and rollback state; do not blindly retry. Globals: document (Document), uiDocument (UIDocument), app (Application). REQUIRES EnableCodeExecution=true in ~/.revitcortex/settings.json.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var settings = CortexSettings.Load();
        if (!settings.EnableCodeExecution)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.PermissionDenied,
                "send_code_to_revit is disabled in this installation. STOP: do NOT retry this tool. Ask the user to enable code execution via Settings > Tools (or \"EnableCodeExecution\": true in ~/.revitcortex/settings.json), or solve the task with dedicated tools instead.",
                suggestion: "Do not retry send_code_to_revit. Either ask the user to enable it in Settings, or use native tools (ai_element_filter, export_elements_data, bulk_modify_parameter_values, etc.) for the same task.");
        }

        var doc = session.Store.Get<object>("activeDocument") as Document;
        if (doc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "No active document in session");

        var code = input["code"]?.Value<string>();
        var transactionMode = input["transactionMode"]?.Value<string>() ?? "auto";
        var modeError = ScriptTransactionMode.Validate(transactionMode);
        if (modeError != null) return modeError;
        var reusable = input["reusable"]?.Value<bool>() ?? false;
        var scriptName = SanitizeName(input["scriptName"]?.Value<string>() ?? "script");

        if (string.IsNullOrEmpty(code))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput, "code is required");

        var sandboxResult = CodeSandbox.Validate(code!);
        if (sandboxResult != null)
            return sandboxResult;

        if (!session.RequestConfirmation("execute C# script", 1, critical: true))
            return CortexResult<object>.Fail(CortexErrorCode.Cancelled, "Script execution cancelled by user");

        var scriptPath = PersistScript(code!, scriptName, reusable);

        var uiApp = session.Store.Get<object>("uiApplication") as Autodesk.Revit.UI.UIApplication;
        var uiDoc = uiApp?.ActiveUIDocument;

        if (uiDoc == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "UIApplication not available in session");

        var globals = new ScriptGlobals
        {
            document = doc,
            uiDocument = uiDoc,
            app = uiApp!.Application
        };

        return RoslynExecutor.Execute(code!, globals, transactionMode, scriptPath,
            reusable ? "REUSABLE" : "TEMP (deleted at Revit close)");
    }

    private static string PersistScript(string code, string scriptName, bool reusable)
    {
        try
        {
            Directory.CreateDirectory(ScriptsFolder);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{stamp}_{scriptName}.cs";
            var filePath = Path.Combine(ScriptsFolder, fileName);

            var lifetime = reusable ? "REUSABLE" : "TEMP";
            var header =
                $"// {lifetime}\n" +
                $"// Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                $"// Name: {scriptName}\n" +
                "// =============================================\n";

            File.WriteAllText(filePath, header + code);
            return filePath;
        }
        catch
        {
            return "(could not save script)";
        }
    }

    public static void CleanupTempScripts()
    {
        if (!Directory.Exists(ScriptsFolder)) return;
        foreach (var file in Directory.GetFiles(ScriptsFolder, "*.cs"))
        {
            try
            {
                using var reader = new StreamReader(file);
                var firstLine = reader.ReadLine() ?? "";
                if (firstLine.TrimStart().StartsWith("// TEMP", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
            catch { }
        }
    }

    private static string SanitizeName(string name)
    {
        var safe = Regex.Replace(name, @"[^\w\-]", "-").Trim('-');
        return safe.Length == 0 ? "script" : safe.Substring(0, Math.Min(safe.Length, 40));
    }
}
