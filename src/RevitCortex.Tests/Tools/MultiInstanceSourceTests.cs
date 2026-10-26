using Xunit;
namespace RevitCortex.Tests.Tools;
public class MultiInstanceSourceTests
{
    private static string Source(string project, string file) => File.ReadAllText(Path.GetFullPath(Path.Combine("..","..","..","..",project,file)));
    [Fact]
    public void ServiceProjectNeverReplacesActiveDocumentOrDeletesFiles()
    {
        var s=Source("RevitCortex.Plugin","RevitCortexApp.cs");
        var begin=s.IndexOf("EnsureServiceDocument(bool requireEmpty)",StringComparison.Ordinal);
        var end=s.IndexOf("private static void WriteStartupFailure",begin,StringComparison.Ordinal);
        var body=s[begin..end];
        Assert.True(body.IndexOf("if (ui.ActiveUIDocument != null)",StringComparison.Ordinal)<body.IndexOf("NewProjectDocument",StringComparison.Ordinal));
        Assert.Contains("if (requireEmpty) return",body);
        Assert.Contains("OverwriteExistingFile = false",body);
        Assert.True(body.IndexOf("seed.Close(false)",StringComparison.Ordinal)<body.IndexOf("OpenAndActivateDocument(path)",StringComparison.Ordinal));
        Assert.DoesNotContain("File.Delete",body);
        Assert.DoesNotContain("new Transaction",body);
        Assert.Contains("serviceProjectPath",body);
        Assert.Contains("cleanupError",body);
    }

    [Fact]
    public void ScriptBootstrapPreservesEnableAndSandboxGates()
    {
        var s=Source("RevitCortex.Tools","Meta/EnsureServiceDocumentTool.cs");
        var create=s.IndexOf("session.EnsureServiceDocument?.Invoke",StringComparison.Ordinal);
        Assert.True(s.IndexOf("CanPrepareScript",StringComparison.Ordinal)<create);
        Assert.True(s.IndexOf("EnableCodeExecution",StringComparison.Ordinal)<create);
        Assert.True(s.IndexOf("CodeSandbox.Validate",StringComparison.Ordinal)<create);
        Assert.Contains("[ToolSafety(false, false)]",s);
    }

    [Fact]
    public void ScriptPersistenceAndCleanupUseSameProcessFolder()
    {
        Assert.Contains("CortexEnvironment.Current.ProcessScriptsFolder",Source("RevitCortex.Tools","Elements/SendCodeToRevitTool.cs"));
        Assert.Contains("CortexEnvironment.Current.ProcessScriptsFolder",Source("RevitCortex.Plugin","RevitCortexApp.cs"));
        Assert.DoesNotContain("GetFiles(scriptsFolder, \"*.cs\",",Source("RevitCortex.Plugin","RevitCortexApp.cs"));
    }
}
