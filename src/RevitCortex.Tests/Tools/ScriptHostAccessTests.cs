using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RevitCortex.Tools.CodeExecution;
using Xunit;

namespace RevitCortex.Tests.Tools;

public class ScriptHostAccessTests
{
    private static readonly string[] Framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
    [Theory]
    [InlineData("RevitCortex.Core.Session.CortexSession", "var x = new X(new RevitCortex.Core.Session.SessionStore()); return x;")]
    [InlineData("RevitCortex.Core.Session.AutomaticExecutionPolicy", "var p = new X(); p.SetEnabled(true); return p;")]
    [InlineData("RevitCortex.Plugin.RevitCortexApp", "X.Instance.Session.CriticalConfirmAction = (a,b,c) => true; return null;")]
    [InlineData("RevitCortex.Plugin.RevitCortexApp", "X.Instance.Router.ReadOnlyMode = false; return null;")]
    [InlineData("RevitCortex.Plugin.RevitCortexApp", "X.Instance.Router.SetDisabledTools(new string[0]); return null;")]
    [InlineData("RevitCortex.Plugin.RevitCortexApp", "X.Instance.SetAutomaticExecution(true); return null;")]
    [InlineData("RevitCortex.Plugin.RevitCortexApp", "return $\"{X.Instance}\";")]
    [InlineData(@"RevitCortex.Plugin.RevitCortex\u0041pp", "return X.Instance;")]
    public void HostControlsAreRejectedBeforeEmissionEvenWithAliases(string type, string body)
    {
        var source = "using X = global::" + type + "; public class Probe { public static object Run() { " + body + " } }";
        var refs = Framework.Append(Path.Combine(AppContext.BaseDirectory, "RevitCortex.Plugin.dll"))
            .Append(typeof(RevitCortex.Core.Session.CortexSession).Assembly.Location).Distinct().ToArray();
        var bytes = RoslynCompilerWorker.Compile(source, refs, 0, "none", out var errors);
        Assert.Null(bytes);
        Assert.Contains(errors, e => e.StartsWith("CORTEX_HOST_ACCESS:"));
    }
    [Fact]
    public void AnonymousRevitLikeDataIsNotRejected()
    {
        var bytes = RoslynCompilerWorker.Compile("public class Probe { public static object Run() => new { Id = 123L, Name = \"RevitCortex.Plugin.RevitCortexApp\" }; }",
            Framework, 0, "none", out var errors);
        Assert.NotNull(bytes); Assert.Empty(errors);
    }
}
