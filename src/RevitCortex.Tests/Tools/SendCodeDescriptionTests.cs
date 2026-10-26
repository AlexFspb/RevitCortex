using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Regression guards for the send_code_to_revit tool guidance.
///
/// The 2026-06-30 audit found the model-facing description had drifted toward a
/// permissive "Execute custom C# code in the Revit context" wording that nudged the
/// model to pick the script tool over dedicated tools (44/54 logged calls failed).
/// These tests pin the de-escalation guidance in the channels the model actually
/// reads: the MCP [Description] attribute on the server method, the server-level
/// ServerInstructions handshake, and the in-Revit UI tool description.
/// </summary>
public class SendCodeDescriptionTests
{
    private static string ServerToolDescription()
    {
        var method = typeof(ProjectTools).GetMethod(
            nameof(ProjectTools.SendCodeToRevit),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(attr);
        return attr!.Description;
    }

    private static string ReadSource(params string[] segments)
    {
        var parts = new string[4 + segments.Length];
        parts[0] = parts[1] = parts[2] = parts[3] = "..";
        Array.Copy(segments, 0, parts, 4, segments.Length);
        var path = Path.GetFullPath(Path.Combine(parts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ServerDescription_MarksToolAsLastResort()
    {
        Assert.Contains("LAST RESORT", ServerToolDescription(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_PreservesAutonomousWorkAndExplainsResultFailures()
    {
        Assert.Contains("no separate chat approval", ServerToolDescription(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ResultSerializationFailed", ServerToolDescription());
        Assert.Contains("transactionState", ServerToolDescription());
        Assert.DoesNotContain("obtaining explicit user consent", ServerToolDescription());
    }

    [Fact]
    public void ServerDescription_PointsToDedicatedAlternatives()
    {
        var desc = ServerToolDescription();
        Assert.Contains("set_element_parameters", desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ai_element_filter", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_ExplainsFamilyEditingPreconditionsAndLimits()
    {
        AssertFamilyEditingGuidance(ServerToolDescription());
    }

    [Fact]
    public void ServerInstructions_ExplainFamilyEditingPreconditionsAndLimits()
    {
        AssertFamilyEditingGuidance(ReadSource("RevitCortex.Server", "Program.cs"));
    }

    [Fact]
    public void PluginDescription_ExplainsFamilyEditingPreconditionsAndLimits()
    {
        AssertFamilyEditingGuidance(ReadSource("RevitCortex.Tools", "Elements", "SendCodeToRevitTool.cs"));
    }

    private static void AssertFamilyEditingGuidance(string text)
    {
        Assert.Contains("strictWarnings=true", text);
        Assert.Contains("before confirmation and persistence", text);
        Assert.Contains("Document.EditFamily is supported in a valid ExternalEvent API context", text);
        Assert.Contains("transactionMode=none, not auto/group", text);
        Assert.Contains("IsModifiable=false", text);
        Assert.Contains("IsReadOnly=false", text);
        Assert.Contains("family ownership", text);
        Assert.Contains("IsEditable=true", text);
        Assert.Contains("IsInPlace=false", text);
        Assert.Contains("never call during dynamic update", text);
        Assert.Contains("failure handling configured before changes", text);
        Assert.Contains("finally", text);
        Assert.Contains("IFamilyLoadOptions", text);
        Assert.Contains("FamilyLoadPolicy", text);
        Assert.Contains("rollbackOnWarnings: true", text);
        Assert.Contains("none/group", text);
        Assert.Contains("capture.Failures", text);
        Assert.Contains("capture.OmittedFailures", text);
        Assert.Contains("capture.DiagnosticReportPath", text);
        Assert.Contains("Close(false)", text);
        Assert.Contains("OverwriteExistingFile=false", text);
        Assert.Contains("Cortex workflow rule", text);
        Assert.Contains("materialized plain data", text);
        Assert.Contains("none cannot roll back earlier commits", text);
        Assert.Contains("a timeout does not abort running Revit code", text);
        Assert.DoesNotContain("Document.EditFamily deadlocks", text);
        Assert.DoesNotContain("Never use Document.EditFamily", text);
    }

    [Fact]
    public void ServerDescription_DropsPermissiveLegacyWording()
    {
        Assert.DoesNotContain("Execute custom C# code in the Revit context", ServerToolDescription());
    }

    [Fact]
    public void ServerInstructions_DeprioritizeSendCode()
    {
        var program = ReadSource("RevitCortex.Server", "Program.cs");
        Assert.Contains("ServerInstructions", program);
        Assert.Contains("LAST RESORT", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PluginDescription_MarksToolAsLastResort()
    {
        var source = ReadSource("RevitCortex.Tools", "Elements", "SendCodeToRevitTool.cs");
        Assert.Contains("LAST RESORT", source, StringComparison.OrdinalIgnoreCase);
    }
}
