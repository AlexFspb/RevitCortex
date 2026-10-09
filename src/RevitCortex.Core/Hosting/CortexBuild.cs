namespace RevitCortex.Core.Hosting;

public static class CortexBuild
{
    public const string Id = "2026.10.09-multi-instance.8";
    public static string CoreModuleId => typeof(CortexBuild).Module.ModuleVersionId.ToString("D");
}
