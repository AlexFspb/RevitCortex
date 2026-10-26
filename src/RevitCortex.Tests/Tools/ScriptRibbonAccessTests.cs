using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RevitCortex.Tools.CodeExecution;
using Xunit;

namespace RevitCortex.Tests.Tools;

// Metadata-only API doubles: no Revit process, posted command or UI interaction.
public sealed class ScriptRibbonApiFixture : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CortexRibbonApi_" + Guid.NewGuid().ToString("N") + ".dll");
    public static string[] Framework { get; } = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator)
        .Where(p => System.IO.Path.GetDirectoryName(p) == System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)).ToArray();
    public ScriptRibbonApiFixture()
    {
        const string api = """
            namespace Autodesk.Revit.UI {
                public class RevitCommandId { public static RevitCommandId LookupCommandId(string id) => null; }
                public class UIApplication {
                    public void PostCommand(RevitCommandId id) { }
                    public UIDocument ActiveUIDocument => null;
                }
                public class UIDocument { public UIApplication Application => null; public Autodesk.Revit.DB.Document Document => null; }
            }
            namespace Autodesk.Revit.DB { public class Document { public string Title => "Test"; } }
            namespace Autodesk.Windows { public static class ComponentManager { public static object Ribbon => null; } }
            """;
        var c = CSharpCompilation.Create("RibbonApiDoubles", new[] { CSharpSyntaxTree.ParseText(api) },
            Framework.Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = c.Emit(Path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }
    public void Dispose() => File.Delete(Path);
}

public class ScriptRibbonAccessTests : IClassFixture<ScriptRibbonApiFixture>
{
    private readonly ScriptRibbonApiFixture _api;
    public ScriptRibbonAccessTests(ScriptRibbonApiFixture api) => _api = api;

    [Theory]
    [InlineData("", "ui.Application.PostCommand(null);", "PostCommand")]
    [InlineData("", @"ui.Application.P\u006fstCommand(null);", "PostCommand")]
    [InlineData("", "ui.Application?.PostCommand(null);", "PostCommand")]
    [InlineData("", "void Send(Autodesk.Revit.UI.UIApplication a) { a.PostCommand(null); } Send(ui.Application);", "PostCommand")]
    [InlineData("using Id = Autodesk.Revit.UI.RevitCommandId;", "System.Action<Id> run = ui.Application.PostCommand;", "PostCommand")]
    [InlineData("using Id = Autodesk.Revit.UI.RevitCommandId;", "var id = Id.LookupCommandId(\"ribbon command\");", "RevitCommandId")]
    [InlineData("using static Autodesk.Revit.UI.RevitCommandId;", "var id = LookupCommandId(\"ribbon command\");", "RevitCommandId")]
    [InlineData("using C = Autodesk.Windows.ComponentManager;", "var ribbon = C.Ribbon;", "ComponentManager")]
    public void RejectsIndirectHostControlBeforeEmission(string imports, string body, string denied)
    {
        var source = imports + " public class Probe { public static void Run(Autodesk.Revit.UI.UIDocument ui) { " + body + " } }";
        var references = ScriptRibbonApiFixture.Framework.Append(_api.Path).ToArray();
        // Establish that this is valid C#, not a missing-reference/compiler-error rejection.
        var c = CSharpCompilation.Create("ValidProbe", new[] { CSharpSyntaxTree.ParseText(source) },
            references.Select(p => MetadataReference.CreateFromFile(p)), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(c.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var bytes = RoslynCompilerWorker.Compile(source, references, 0, "none", out var errors);
        Assert.Null(bytes);
        Assert.Contains(errors, e => e.StartsWith("CORTEX_HOST_ACCESS:") && e.Contains(denied));
    }

    [Fact]
    public void ModelAccessAndUnrelatedUserMethodNamedPostCommandRemainAvailable()
    {
        const string source = """
            public class Probe {
                private static void PostCommand(string value) { }
                public static object Run(Autodesk.Revit.UI.UIDocument ui) {
                    PostCommand("local helper");
                    return new { Title = ui.Application.ActiveUIDocument.Document.Title };
                }
            }
            """;
        var bytes = RoslynCompilerWorker.Compile(source, ScriptRibbonApiFixture.Framework.Append(_api.Path).ToArray(), 0, "none", out var errors);
        Assert.NotNull(bytes);
        Assert.Empty(errors);
    }
}
