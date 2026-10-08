using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RevitCortex.Core.Results;
using RevitCortex.Tools.CodeExecution;
using Xunit;

namespace RevitCortex.Tests.Tools;

// These tests compile against API doubles, not native Revit. They execute the real guard
// source and compiler rewriter, checking that forbidden calls never reach the API double.
public class FamilyEditGuardTests
{
    private static string GuardSource => File.ReadAllText(Path.GetFullPath(Path.Combine(
        "..", "..", "..", "..", "RevitCortex.Tools", "CodeExecution", "ScriptFamilyEditGuard.cs")));

    private const string FakeApi = """
        namespace Autodesk.Revit.UI { public class UIDocument { } }
        namespace Autodesk.Revit.ApplicationServices { public class Application { } }
        namespace Autodesk.Revit.DB {
            public enum FamilySource { Project, Family }
            public interface IFamilyLoadOptions {
                bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues);
                bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues);
            }
            public class Document {
                public bool IsValidObject { get; set; } = true;
                public bool IsModifiable { get; set; }
                public bool IsReadOnly { get; set; }
                public static int Calls;
                public Document EditFamily(Family loadedFamily) { Calls++; return this; }
            }
            public class Family {
                public bool IsValidObject { get; set; } = true;
                public bool IsEditable { get; set; } = true;
                public bool IsInPlace { get; set; }
                public Document Document { get; set; }
            }
        }
        """;

    private static readonly string[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => Path.GetDirectoryName(p) == Path.GetDirectoryName(typeof(object).Assembly.Location))
        .Append(typeof(ScriptPreconditionException).Assembly.Location)
        .Append(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location).Distinct().ToArray();

    // Separate helper assembly prevents the rewriter from instrumenting its own native call.
    private static readonly Lazy<string> HelperAssembly = new(() =>
    {
        var helperText = GuardSource.Replace("namespace RevitCortex.Tools.CodeExecution;",
            "namespace RevitCortex.Tools.CodeExecution {") + "\n}";
        var policyText = File.ReadAllText(Path.GetFullPath(Path.Combine("..", "..", "..", "..",
            "RevitCortex.Tools", "CodeExecution", "FamilyLoadPolicy.cs")));
        var compilation = CSharpCompilation.Create("CortexGuardDoubles_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(FakeApi), CSharpSyntaxTree.ParseText(helperText), CSharpSyntaxTree.ParseText(policyText) },
            References.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        // The file must survive all cases and repeated worker compilations. Test-run temp only.
        var path = Path.Combine(Path.GetTempPath(), compilation.AssemblyName + ".dll");
        var emitted = compilation.Emit(path);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        return path;
    });

    private static (byte[]? Bytes, string[] Errors) Compile(string body, string mode = "none")
    {
        var source = "using System; using Autodesk.Revit.DB; public static class Probe { public static object Run() { " + body + " } }";
        var bytes = RoslynCompilerWorker.Compile(source, References.Append(HelperAssembly.Value).ToArray(), 0, mode, out var errors);
        return (bytes, errors);
    }

    private static object Run(string body)
    {
        var result = Compile(body);
        Assert.True(result.Bytes != null, string.Join("\n", result.Errors));
        return Assembly.Load(result.Bytes!).GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)!;
    }

    private const string Setup = "var doc = new Document(); var f = new Family { Document = doc }; Document.Calls = 0; ";

    [Theory]
    [InlineData("auto")]
    [InlineData("group")]
    public void WrongModesRejectBeforeEmittingExecutableCode(string mode)
    {
        var result = Compile(Setup + "return doc.EditFamily(f);", mode);
        Assert.Null(result.Bytes);
        Assert.Contains(result.Errors, e => e.Contains("transactionMode=none"));
    }

    [Theory]
    [InlineData("doc.IsModifiable = true;", "OpenTransaction")]
    [InlineData("doc.IsReadOnly = true;", "ReadOnlyDocument")]
    [InlineData("doc.IsValidObject = false;", "InvalidDocument")]
    [InlineData("f.IsValidObject = false;", "InvalidFamily")]
    [InlineData("f.IsInPlace = true;", "FamilyNotEditable")]
    [InlineData("f.IsEditable = false;", "FamilyNotEditable")]
    [InlineData("f.Document = new Document();", "FamilyDocumentMismatch")]
    public void RewrittenCallRechecksRuntimeStateBeforeNativeCall(string change, string reason)
    {
        var value = Run(Setup + change + """
            try { doc.EditFamily(f); return "UNEXPECTED"; }
            catch (RevitCortex.Core.Results.ScriptPreconditionException e) {
                return e.Reason + ":" + Document.Calls;
            }
            """);
        Assert.Equal(reason + ":0", value);
    }

    [Theory]
    [InlineData("doc.EditFamily(f)")]
    [InlineData("doc.@EditFamily(loadedFamily: f)")]
    [InlineData("doc.\\u0045ditFamily(f)")]
    [InlineData("((Document)doc).EditFamily(f)")]
    [InlineData("doc /* comment */ . EditFamily(f)")]
    public void ValidCallsAreForwardedOnce(string expression)
    {
        Assert.Equal(1, Run(Setup + expression + "; return Document.Calls;"));
    }

    [Fact]
    public void LocalFunctionChecksAtInvocationNotAtCompilation()
    {
        Assert.Equal("OpenTransaction:0", Run(Setup + """
            Document Open() => doc.EditFamily(f);
            doc.IsModifiable = true;
            try { Open(); return "UNEXPECTED"; }
            catch (RevitCortex.Core.Results.ScriptPreconditionException e) { return e.Reason + ":" + Document.Calls; }
            """));
    }

    [Theory]
    [InlineData("Func<Family, Document> open = doc.EditFamily; return open(f);")]
    [InlineData("return doc?.EditFamily(f);")]
    public void UnsupportedCallShapesFailClosed(string body)
    {
        var result = Compile(Setup + body);
        Assert.Null(result.Bytes);
        Assert.Contains(result.Errors, e => e.StartsWith("CORTEX_FAMILY_GUARD:"));
    }

    [Fact]
    public void TextAndNameofDoNotBlockUnrelatedAutoScripts()
    {
        var result = Compile(Setup + "// doc.EditFamily(f)\nreturn nameof(doc.EditFamily) + \"doc.EditFamily(f)\";", "auto");
        Assert.NotNull(result.Bytes);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("group")]
    [InlineData("none")]
    public void EntryGuardBlocksExistingTransactionWithoutEndingIt(string mode)
    {
        var value = Run(Setup + "doc.IsModifiable = true; " +
            "var r = RevitCortex.Tools.CodeExecution.ScriptFamilyEditGuard.CheckStart(doc, \"" + mode + "\"); " +
            "return r.Error.Code.ToString() + \":\" + doc.IsModifiable + \":\" + Document.Calls;");
        Assert.Equal("ScriptPreconditionFailed:True:0", value);
    }

    [Fact]
    public void NoneStillAllowsOrdinaryReadsInReadOnlyDocument()
    {
        Assert.Equal(true, Run(Setup + "doc.IsReadOnly = true; return RevitCortex.Tools.CodeExecution.ScriptFamilyEditGuard.CheckStart(doc, \"none\") == null;"));
    }

    [Fact]
    public void ReceiverAndFamilyArgumentsAreEvaluatedOnlyOnce()
    {
        Assert.Equal("1:1:1", Run(Setup + """
            int receivers = 0, families = 0;
            Document GetDoc() { receivers++; return doc; }
            Family GetFamily() { families++; return f; }
            GetDoc().EditFamily(GetFamily());
            return receivers + ":" + families + ":" + Document.Calls;
            """));
    }

    [Fact]
    public void LambdaAndInterpolatedExpressionAreGuarded()
    {
        Assert.Equal("OpenTransaction:0", Run(Setup + """
            Func<string> open = () => $"{doc.EditFamily(f)}";
            doc.IsModifiable = true;
            try { open(); return "UNEXPECTED"; }
            catch (RevitCortex.Core.Results.ScriptPreconditionException e) { return e.Reason + ":" + Document.Calls; }
            """));
    }

    [Fact]
    public void StructuredErrorDistinguishesEntryRejectionFromEarlierEffects()
    {
        var error = new ScriptPreconditionException("OpenTransaction", "Document.EditFamily", "Blocked");
        var entry = error.ToFailure(false);
        var runtime = error.ToFailure(true);
        Assert.Equal(CortexErrorCode.ScriptPreconditionFailed, entry.Error!.Code);
        Assert.Equal(false, entry.Error.Context!["scriptExecuted"]);
        Assert.Equal(false, entry.Error.Context["externalEffectsMayRemain"]);
        Assert.Equal(true, runtime.Error!.Context!["scriptExecuted"]);
        Assert.Equal(true, runtime.Error.Context["externalEffectsMayRemain"]);
        Assert.Equal("not_managed", runtime.Error.Context["transactionState"]);
    }

    [Fact]
    public void EntryGuardIsWiredBeforeAndAfterConfirmationAndInExecutor()
    {
        var root = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "RevitCortex.Tools"));
        var tool = File.ReadAllText(Path.Combine(root, "Elements", "SendCodeToRevitTool.cs"));
        var first = tool.IndexOf("ScriptFamilyEditGuard.CheckStart(doc, transactionMode)", StringComparison.Ordinal);
        var confirm = tool.IndexOf("session.RequestConfirmation", StringComparison.Ordinal);
        var second = tool.IndexOf("ScriptFamilyEditGuard.CheckStart(doc, transactionMode)", first + 1, StringComparison.Ordinal);
        var persist = tool.IndexOf("var scriptPath = PersistScript", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < confirm && confirm < second && second < persist);
        var executor = File.ReadAllText(Path.Combine(root, "CodeExecution", "RoslynExecutor.cs"));
        var preflight = tool.IndexOf("RoslynExecutor.TryPrepare", StringComparison.Ordinal);
        Assert.True(first < preflight && preflight < confirm);
        Assert.Contains("if (compileError != null) return compileError;", tool);
        Assert.Contains("RoslynExecutor.ExecutePrepared(prepared!", tool);
        var execute = executor.IndexOf("internal static CortexResult<object> ExecutePrepared", StringComparison.Ordinal);
        Assert.True(executor.IndexOf("ScriptFamilyEditGuard.CheckStart", execute, StringComparison.Ordinal) <
                    executor.IndexOf("Assembly.Load(script.AssemblyBytes)", execute, StringComparison.Ordinal));
        Assert.Contains("ex.InnerException is ScriptPreconditionException", executor);
        Assert.Contains("ToFailure(scriptExecuted: true)", executor);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LoadPolicyIsUsableInsideMethodAndPreservesExplicitDecisions(bool overwrite, bool projectShared)
    {
        string value = (string)Run(Setup +
            $"var policy = new RevitCortex.Tools.CodeExecution.FamilyLoadPolicy({overwrite.ToString().ToLowerInvariant()}, {projectShared.ToString().ToLowerInvariant()}); " + """
            bool ordinary = policy.OnFamilyFound(true, out var overwriteOrdinary);
            bool shared = policy.OnSharedFamilyFound(f, false, out var source, out var overwriteShared);
            return ordinary + ":" + shared + ":" + overwriteOrdinary + ":" + overwriteShared + ":" + source;
            """);
        Assert.Equal($"True:True:{overwrite}:{overwrite}:{(projectShared ? "Project" : "Family")}", value);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("group")]
    public void UnrelatedLocalFunctionNamedEditFamilyIsNotRejected(string mode)
    {
        var result = Compile("int EditFamily(int x) => x; return EditFamily(1);", mode);
        Assert.NotNull(result.Bytes);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("auto")]
    public void InvalidOverloadRetainsOriginalArgumentDiagnostics(string mode)
    {
        // Rewriting would change the failing argument from 1 to 2 (inserted document).
        var result = Compile(Setup + "return doc.EditFamily(123);", mode);
        Assert.Null(result.Bytes);
        Assert.Contains(result.Errors, e => e.Contains("Argument 1:") && e.Contains("Family"));
        Assert.DoesNotContain(result.Errors, e => e.Contains("Argument 2:"));
        Assert.Equal(mode == "auto", result.Errors.Any(e => e.StartsWith("CORTEX_FAMILY_GUARD:")));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("group")]
    public void PolicyFailureAndOrdinaryCompileErrorsAreBothReported(string mode)
    {
        var result = Compile(Setup + "doc.EditFamily(f); return missingVariable;", mode);
        Assert.Null(result.Bytes);
        Assert.Contains(result.Errors, e => e.StartsWith("CORTEX_FAMILY_GUARD:"));
        Assert.Contains(result.Errors, e => e.Contains("missingVariable"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    public void RealWrapperReportsUserLineNumbers(int line, bool policyFailure)
    {
        var body = new string('\n', line - 1) + (policyFailure
            ? "return document.EditFamily(new Family());" : "return missingVariable;");
        var bytes = RoslynCompilerWorker.Compile(RoslynExecutor.WrapCode(body),
            References.Append(HelperAssembly.Value).ToArray(), RoslynExecutor.PrefixLines, "auto", out var errors);
        Assert.Null(bytes);
        Assert.Single(errors);
        Assert.StartsWith((policyFailure ? "CORTEX_FAMILY_GUARD: " : "") + $"Line {line}:", errors[0]);
    }

    [Fact]
    public void LoadPolicyHasNoImplicitReloadChoices()
    {
        var result = Compile("return new RevitCortex.Tools.CodeExecution.FamilyLoadPolicy();");
        Assert.Null(result.Bytes);
        Assert.Contains(result.Errors, e => e.Contains("overwriteParameterValues"));
    }
}
