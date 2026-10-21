namespace RevitCortex.Core.Hosting;

public static class CortexBuild
{
    public const string Id = "2026.10.21-ribbon-activity.10";
    public static string CoreModuleId => typeof(CortexBuild).Module.ModuleVersionId.ToString("D");
}
