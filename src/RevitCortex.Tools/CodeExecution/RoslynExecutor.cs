using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;

namespace RevitCortex.Tools.CodeExecution;

/// <summary>
/// Compiles and executes C# code snippets inside Autodesk Revit 2026 / .NET 8.
///
/// Roslyn isolation: Revit hosts add-ins in a shared AssemblyLoadContext, and sibling
/// add-ins may ship different System.Collections.Immutable / System.Reflection.Metadata
/// versions. The compilation step is therefore delegated to RoslynCompilerWorker loaded
/// into a dedicated RoslynLoadContext that resolves Roslyn and its dependencies from the
/// RevitCortex plugin folder.
/// </summary>
public static class RoslynExecutor
{
    private static readonly int PrefixLines = 12;

    private static MethodInfo? _compileMethod;
    private static readonly object _compileLock = new object();

    public static CortexResult<object> Execute(
        string code,
        ScriptGlobals globals,
        string transactionMode = "auto",
        string? scriptPath = null,
        string? scriptLifetime = null)
    {
        try
        {
            var wrappedCode = WrapCode(code);
            var referencePaths = GatherReferencePaths();

            byte[]? assemblyBytes;
            string[] compileErrors;
            try
            {
                var compile = GetCompileMethod();
                var args = new object?[] { wrappedCode, referencePaths.ToArray(), PrefixLines, null };
                assemblyBytes = (byte[]?)compile.Invoke(null, args);
                compileErrors = (string[])args[3]! ?? Array.Empty<string>();
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                return CortexResult<object>.Fail(
                    CortexErrorCode.Unknown,
                    $"Roslyn compilation failed: {ex.InnerException.Message}",
                    suggestion: "This is an internal compiler/assembly-loading error, not a problem with your code.");
            }

            if (assemblyBytes == null)
            {
                return CortexResult<object>.Fail(
                    CortexErrorCode.InvalidInput,
                    $"Compilation error:\n{string.Join("\n", compileErrors)}",
                    suggestion: "Globals: document (Document), uiDocument (UIDocument), app (Application). Use explicit 'return'.");
            }

            var assembly = Assembly.Load(assemblyBytes);
            var type = assembly.GetType("RevitCortex.DynamicScript.ScriptRunner")!;
            var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;

            JObject prepared;
            JObject Prepare() => SafeScriptResultProjector.Project(
                method.Invoke(null, new object[] { globals.document, globals.uiDocument, globals.app }),
                scriptPath, scriptLifetime);

            if (transactionMode == "none")
            {
                prepared = Prepare();
            }
            else if (transactionMode == "group")
            {
                using var txGroup = new TransactionGroup(globals.document, "RevitCortex: Script Group");
                txGroup.Start();
                try
                {
                    prepared = Prepare();
                    var groupStatus = txGroup.GetStatus();
                    if (groupStatus == TransactionStatus.Started) groupStatus = txGroup.Assimilate();
                    if (groupStatus != TransactionStatus.Committed)
                    {
                        return CortexResult<object>.Fail(
                            CortexErrorCode.TransactionFailed,
                            groupStatus == TransactionStatus.RolledBack
                                ? "Revit rolled back the script transaction group."
                                : "Revit did not commit the script transaction group; rollback is not confirmed.",
                            suggestion: "Inspect Revit failures and verify model state before retrying. Do not retry automatically.",
                            context: new Dictionary<string, object> { ["transactionState"] = groupStatus.ToString() });
                    }
                }
                catch (ScriptResultException ex)
                {
                    return ex.ToFailure(TryRollback(txGroup.GetStatus, txGroup.RollBack));
                }
                catch
                {
                    if (txGroup.GetStatus() == TransactionStatus.Started)
                        txGroup.RollBack();
                    throw;
                }
            }
            else
            {
                using var tx = new Transaction(globals.document, "RevitCortex: Script");
                tx.Start();
                var txFailures = ScriptFailureHandling.Configure(tx);
                try
                {
                    prepared = Prepare();
                    var status = tx.GetStatus();
                    if (status == TransactionStatus.Started) status = tx.Commit();
                    if (status != TransactionStatus.Committed) return txFailures.ToFailure(status);
                }
                catch (ScriptResultException ex)
                {
                    return ex.ToFailure(TryRollback(tx.GetStatus, tx.RollBack));
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();
                    throw;
                }
            }

            return CortexResult<object>.Ok(prepared);
        }
        catch (ScriptResultException ex)
        {
            return ex.ToFailure("not_managed");
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.Unknown,
                $"Runtime error: {ex.InnerException}",
                suggestion: "Check variable names, null references, and Revit API usage. Full stack trace above.");
        }
        catch (Exception ex)
        {
            return CortexResult<object>.Fail(
                CortexErrorCode.Unknown,
                $"Execution error: {ex.Message}");
        }
    }

    private static MethodInfo GetCompileMethod()
    {
        if (_compileMethod != null)
            return _compileMethod;

        lock (_compileLock)
        {
            if (_compileMethod != null)
                return _compileMethod;

            var dir = Path.GetDirectoryName(typeof(RoslynExecutor).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
                dir = AppContext.BaseDirectory;

            var alc = new RoslynLoadContext(dir!);
            var toolsAsm = alc.LoadFromAssemblyPath(Path.Combine(dir!, "RevitCortex.Tools.dll"));
            var workerType = toolsAsm.GetType("RevitCortex.Tools.CodeExecution.RoslynCompilerWorker", throwOnError: true)!;
            _compileMethod = workerType.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("RoslynCompilerWorker.Compile not found");
            return _compileMethod;
        }
    }

    private sealed class RoslynLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;

        public RoslynLoadContext(string dir) : base(name: "RevitCortexRoslyn", isCollectible: false)
        {
            _dir = dir;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            bool isolate = name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || name == "System.Collections.Immutable"
                || name == "System.Reflection.Metadata"
                || name == "RevitCortex.Tools";

            if (isolate)
            {
                var path = Path.Combine(_dir, name + ".dll");
                if (File.Exists(path))
                    return LoadFromAssemblyPath(path);
            }

            return null;
        }
    }

    private static string WrapCode(string userCode)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Autodesk.Revit.DB;");
        sb.AppendLine("using Autodesk.Revit.UI;");
        sb.AppendLine("namespace RevitCortex.DynamicScript {");
        sb.AppendLine("public static class ScriptRunner {");
        sb.AppendLine("public static object? Run(Document document, UIDocument uiDocument, Autodesk.Revit.ApplicationServices.Application app) {");
        sb.AppendLine("// ---- user code ----");
        sb.AppendLine(userCode);
        sb.AppendLine("return null;");
        sb.AppendLine("} } }");
        return sb.ToString();
    }

    private static List<string> GatherReferencePaths()
    {
        var refs = new List<string>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (!asm.IsDynamic && !string.IsNullOrEmpty(asm.Location) && File.Exists(asm.Location))
                    refs.Add(asm.Location);
            }
            catch { }
        }
        return refs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string TryRollback(Func<TransactionStatus> status, Func<TransactionStatus> rollback)
    {
        try
        {
            var current = status();
            if (current == TransactionStatus.Started) current = rollback();
            return current == TransactionStatus.RolledBack ? "rolled_back" : "not_rolled_back";
        }
        catch { return "unknown"; }
    }
}
